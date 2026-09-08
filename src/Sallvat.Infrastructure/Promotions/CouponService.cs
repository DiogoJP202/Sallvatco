using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sallvat.Application.Promotions;
using Sallvat.Application.Time;
using Sallvat.Domain.Auditing;
using Sallvat.Domain.Promotions;
using Sallvat.Infrastructure.Persistence;

namespace Sallvat.Infrastructure.Promotions;

internal sealed class CouponService(
    SallvatDbContext dbContext,
    IClock clock) : ICouponService
{
    private static readonly SemaphoreSlim ReservationLock = new(1, 1);

    public async Task<IReadOnlyList<AdminCouponSummary>> ListAdminAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.Coupons
            .AsNoTracking()
            .OrderByDescending(coupon => coupon.IsActive)
            .ThenBy(coupon => coupon.Code)
            .Select(coupon => new AdminCouponSummary(
                coupon.Id,
                coupon.Code,
                coupon.DiscountType,
                coupon.Value,
                coupon.MinimumSubtotal,
                coupon.ClaimedUsageCount,
                coupon.TotalUsageLimit,
                coupon.IsActive,
                coupon.ExpiresAtUtc,
                coupon.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

    public Task<AdminCouponDetails?> GetAdminAsync(
        long id,
        CancellationToken cancellationToken = default) =>
        dbContext.Coupons
            .AsNoTracking()
            .Where(coupon => coupon.Id == id)
            .Select(coupon => new AdminCouponDetails(
                coupon.Id,
                new CouponEditorInput(
                    coupon.Code,
                    coupon.DiscountType,
                    coupon.Value,
                    coupon.MinimumSubtotal,
                    coupon.StartsAtUtc,
                    coupon.ExpiresAtUtc,
                    coupon.TotalUsageLimit,
                    coupon.UsageLimitPerIdentity,
                    coupon.IsActive),
                coupon.ClaimedUsageCount,
                coupon.ConcurrencyVersion,
                coupon.CreatedAtUtc,
                coupon.UpdatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<CouponMutationResult> CreateAsync(
        CouponEditorInput input,
        PromotionAdminContext operation,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedCode = CouponCode.Normalize(input.Code);
            if (await dbContext.Coupons.AnyAsync(
                    coupon => coupon.NormalizedCode == normalizedCode,
                    cancellationToken))
            {
                return Duplicate();
            }

            await using var transaction = await BeginTransactionAsync(
                cancellationToken);
            var coupon = CreateCoupon(input);
            dbContext.Coupons.Add(coupon);
            await dbContext.SaveChangesAsync(cancellationToken);
            AddAudit(operation, "CouponCreated", coupon, new
            {
                coupon.Code,
                coupon.DiscountType,
                coupon.Value,
                coupon.IsActive,
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            return CouponMutationResult.Success(coupon.Id);
        }
        catch (DbUpdateException)
        {
            return Duplicate();
        }
        catch (ArgumentException exception)
        {
            return Invalid(exception);
        }
    }

    public async Task<CouponMutationResult> UpdateAsync(
        long id,
        Guid concurrencyVersion,
        CouponEditorInput input,
        PromotionAdminContext operation,
        CancellationToken cancellationToken = default)
    {
        var coupon = await dbContext.Coupons.SingleOrDefaultAsync(
            candidate => candidate.Id == id,
            cancellationToken);
        if (coupon is null)
        {
            return NotFound();
        }

        if (coupon.ConcurrencyVersion != concurrencyVersion)
        {
            return Conflict();
        }

        try
        {
            var normalizedCode = CouponCode.Normalize(input.Code);
            if (await dbContext.Coupons.AnyAsync(
                    candidate => candidate.Id != id
                        && candidate.NormalizedCode == normalizedCode,
                    cancellationToken))
            {
                return Duplicate();
            }

            coupon.Update(
                input.Code,
                input.DiscountType,
                input.Value,
                input.MinimumSubtotal,
                input.StartsAtUtc,
                input.ExpiresAtUtc,
                input.TotalUsageLimit,
                input.UsageLimitPerIdentity,
                input.IsActive,
                clock.UtcNow);
            AddAudit(operation, "CouponUpdated", coupon, new
            {
                coupon.Code,
                coupon.DiscountType,
                coupon.Value,
                coupon.MinimumSubtotal,
                coupon.IsActive,
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            return CouponMutationResult.Success(coupon.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict();
        }
        catch (Exception exception)
            when (exception is ArgumentException or InvalidOperationException)
        {
            return Invalid(exception);
        }
    }

    public async Task<CouponMutationResult> SetActiveAsync(
        long id,
        Guid concurrencyVersion,
        bool isActive,
        PromotionAdminContext operation,
        CancellationToken cancellationToken = default)
    {
        var coupon = await dbContext.Coupons.SingleOrDefaultAsync(
            candidate => candidate.Id == id,
            cancellationToken);
        if (coupon is null)
        {
            return NotFound();
        }

        if (coupon.ConcurrencyVersion != concurrencyVersion)
        {
            return Conflict();
        }

        try
        {
            coupon.SetActive(isActive, clock.UtcNow);
            AddAudit(operation, "CouponActivationChanged", coupon, new
            {
                coupon.IsActive,
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            return CouponMutationResult.Success(coupon.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict();
        }
    }

    public async Task<CouponReservationResult> ReserveAsync(
        CouponReservationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ReservationKey == Guid.Empty
            || request.Lines.Count == 0
            || request.ExpiresAtUtc.Offset != TimeSpan.Zero
            || request.ExpiresAtUtc <= clock.UtcNow)
        {
            return ReservationFailure(
                CouponMutationStatus.Invalid,
                "A solicitação de reserva do cupom é inválida.");
        }

        await ReservationLock.WaitAsync(cancellationToken);
        try
        {
            var existing = await dbContext.CouponRedemptions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    redemption => redemption.ReservationKey
                        == request.ReservationKey,
                    cancellationToken);
            if (existing is not null)
            {
                var existingCoupon = await dbContext.Coupons
                    .AsNoTracking()
                    .SingleAsync(
                        coupon => coupon.Id == existing.CouponId,
                        cancellationToken);
                return existing.Status == CouponRedemptionStatus.Released
                    ? ReservationFailure(
                        CouponMutationStatus.Unavailable,
                        "A reserva deste cupom já foi liberada.")
                    : CouponReservationResult.Success(new CouponReservation(
                        existing.Id,
                        existing.ReservationKey,
                        existing.CouponId,
                        existingCoupon.Code,
                        existing.DiscountAmount,
                        existing.ExpiresAtUtc,
                        []));
            }

            string normalizedCode;
            try
            {
                normalizedCode = CouponCode.Normalize(request.Code);
            }
            catch (ArgumentException exception)
            {
                return ReservationFailure(
                    CouponMutationStatus.Invalid,
                    exception.Message);
            }

            await using var transaction = await BeginTransactionAsync(
                cancellationToken);
            var coupon = await dbContext.Coupons.SingleOrDefaultAsync(
                candidate => candidate.NormalizedCode == normalizedCode,
                cancellationToken);
            if (coupon is null)
            {
                return ReservationFailure(
                    CouponMutationStatus.NotFound,
                    "Cupom não encontrado.");
            }

            var customerId = await ResolveCustomerIdAsync(
                request.ApplicationUserId,
                cancellationToken);
            string? email;
            try
            {
                email = NormalizeEmail(request.Email);
            }
            catch (ArgumentException exception)
            {
                return ReservationFailure(
                    CouponMutationStatus.Invalid,
                    exception.Message);
            }

            if (customerId is null && email is null)
            {
                return ReservationFailure(
                    CouponMutationStatus.Invalid,
                    "Informe o cliente autenticado ou o e-mail do comprador.");
            }

            var statuses = new[]
            {
                CouponRedemptionStatus.Reserved,
                CouponRedemptionStatus.Consumed,
            };
            var identityUsage = await dbContext.CouponRedemptions.CountAsync(
                redemption => redemption.CouponId == coupon.Id
                    && statuses.Contains(redemption.Status)
                    && (customerId.HasValue
                        ? redemption.CustomerId == customerId
                            || email != null
                                && redemption.NormalizedEmail == email
                        : redemption.CustomerId == null
                            && redemption.NormalizedEmail == email),
                cancellationToken);
            var quote = CouponDiscountCalculator.Calculate(
                coupon.DiscountType,
                coupon.Value,
                request.Lines);
            var eligibility = coupon.Evaluate(
                quote.Subtotal,
                identityUsage,
                clock.UtcNow);
            if (eligibility != CouponEligibilityStatus.Eligible
                || quote.DiscountTotal <= 0)
            {
                return ReservationFailure(
                    CouponMutationStatus.Unavailable,
                    EligibilityMessage(eligibility, coupon.MinimumSubtotal));
            }

            try
            {
                coupon.ClaimUsage(clock.UtcNow);
                var redemption = new CouponRedemption(
                    coupon.Id,
                    request.ReservationKey,
                    customerId,
                    email,
                    quote.DiscountTotal,
                    clock.UtcNow,
                    request.ExpiresAtUtc);
                dbContext.CouponRedemptions.Add(redemption);
                await dbContext.SaveChangesAsync(cancellationToken);
                await CommitAsync(transaction, cancellationToken);
                return CouponReservationResult.Success(new CouponReservation(
                    redemption.Id,
                    redemption.ReservationKey,
                    coupon.Id,
                    coupon.Code,
                    quote.DiscountTotal,
                    redemption.ExpiresAtUtc,
                    quote.Allocations));
            }
            catch (DbUpdateConcurrencyException)
            {
                await RollbackAsync(transaction, cancellationToken);
                return ReservationFailure(
                    CouponMutationStatus.ConcurrencyConflict,
                    "O limite do cupom mudou. Tente novamente.");
            }
        }
        finally
        {
            ReservationLock.Release();
        }
    }

    public async Task<CouponMutationResult> ConsumeAsync(
        Guid reservationKey,
        long orderId,
        CancellationToken cancellationToken = default)
    {
        var redemption = await dbContext.CouponRedemptions.SingleOrDefaultAsync(
            candidate => candidate.ReservationKey == reservationKey,
            cancellationToken);
        if (redemption is null)
        {
            return NotFound();
        }

        try
        {
            redemption.Consume(orderId, clock.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
            return CouponMutationResult.Success(redemption.Id);
        }
        catch (InvalidOperationException exception)
        {
            return CouponMutationResult.Failure(
                CouponMutationStatus.Unavailable,
                exception.Message);
        }
    }

    public async Task<CouponMutationResult> ReleaseAsync(
        Guid reservationKey,
        CancellationToken cancellationToken = default)
    {
        await ReservationLock.WaitAsync(cancellationToken);
        try
        {
            var redemption = await dbContext.CouponRedemptions.SingleOrDefaultAsync(
                candidate => candidate.ReservationKey == reservationKey,
                cancellationToken);
            if (redemption is null)
            {
                return NotFound();
            }

            if (redemption.Status == CouponRedemptionStatus.Released)
            {
                return CouponMutationResult.Success(redemption.Id);
            }

            if (redemption.Status == CouponRedemptionStatus.Consumed)
            {
                return CouponMutationResult.Failure(
                    CouponMutationStatus.Unavailable,
                    "Cupom já consumido por um pedido.");
            }

            var coupon = await dbContext.Coupons.SingleAsync(
                candidate => candidate.Id == redemption.CouponId,
                cancellationToken);
            redemption.Release(clock.UtcNow);
            coupon.ReleaseUsage(clock.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
            return CouponMutationResult.Success(redemption.Id);
        }
        finally
        {
            ReservationLock.Release();
        }
    }

    public async Task<int> ReleaseExpiredAsync(
        int maximumItems,
        CancellationToken cancellationToken = default)
    {
        if (maximumItems is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumItems));
        }

        await ReservationLock.WaitAsync(cancellationToken);
        try
        {
            var expired = await dbContext.CouponRedemptions
                .Where(redemption =>
                    redemption.Status == CouponRedemptionStatus.Reserved
                    && redemption.ExpiresAtUtc <= clock.UtcNow)
                .OrderBy(redemption => redemption.ExpiresAtUtc)
                .Take(maximumItems)
                .ToListAsync(cancellationToken);
            if (expired.Count == 0)
            {
                return 0;
            }

            var couponIds = expired.Select(item => item.CouponId).Distinct();
            var coupons = await dbContext.Coupons
                .Where(coupon => couponIds.Contains(coupon.Id))
                .ToDictionaryAsync(coupon => coupon.Id, cancellationToken);
            foreach (var redemption in expired)
            {
                redemption.Release(clock.UtcNow);
                coupons[redemption.CouponId].ReleaseUsage(clock.UtcNow);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return expired.Count;
        }
        finally
        {
            ReservationLock.Release();
        }
    }

    private Coupon CreateCoupon(CouponEditorInput input) => new(
        input.Code,
        input.DiscountType,
        input.Value,
        input.MinimumSubtotal,
        input.StartsAtUtc,
        input.ExpiresAtUtc,
        input.TotalUsageLimit,
        input.UsageLimitPerIdentity,
        input.IsActive,
        clock.UtcNow);

    private async Task<long?> ResolveCustomerIdAsync(
        Guid? applicationUserId,
        CancellationToken cancellationToken)
    {
        if (!applicationUserId.HasValue
            || applicationUserId.Value == Guid.Empty)
        {
            return null;
        }

        return await dbContext.Customers
            .Where(customer =>
                customer.ApplicationUserId == applicationUserId.Value)
            .Select(customer => (long?)customer.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private void AddAudit(
        PromotionAdminContext operation,
        string action,
        Coupon coupon,
        object changes) =>
        dbContext.AuditLogs.Add(new AuditLog(
            operation.ActorUserId,
            action,
            nameof(Coupon),
            coupon.Id.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(changes),
            operation.CorrelationId,
            clock.UtcNow));

    private async Task<IDbContextTransaction?> BeginTransactionAsync(
        CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

    private static async Task CommitAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static async Task RollbackAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }

    private static string? NormalizeEmail(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToUpperInvariant() is var normalized
                && normalized.Length <= CouponRedemption.NormalizedEmailMaxLength
                && normalized.Contains('@')
                    ? normalized
                    : throw new ArgumentException("E-mail inválido.");

    internal static string EligibilityMessage(
        CouponEligibilityStatus status,
        decimal minimumSubtotal) => status switch
        {
            CouponEligibilityStatus.Eligible => string.Empty,
            CouponEligibilityStatus.Inactive => "Este cupom está inativo.",
            CouponEligibilityStatus.NotStarted => "Este cupom ainda não começou.",
            CouponEligibilityStatus.Expired => "Este cupom expirou.",
            CouponEligibilityStatus.MinimumSubtotalNotMet =>
                $"O subtotal mínimo deste cupom é {minimumSubtotal:C}.",
            CouponEligibilityStatus.GlobalLimitReached =>
                "O limite total deste cupom foi atingido.",
            CouponEligibilityStatus.IdentityLimitReached =>
                "O limite deste cupom por cliente foi atingido.",
            _ => "Este cupom não está disponível.",
        };

    private static CouponMutationResult NotFound() =>
        CouponMutationResult.Failure(
            CouponMutationStatus.NotFound,
            "Cupom não encontrado.");

    private static CouponMutationResult Duplicate() =>
        CouponMutationResult.Failure(
            CouponMutationStatus.Duplicate,
            "Já existe um cupom com este código.");

    private static CouponMutationResult Conflict() =>
        CouponMutationResult.Failure(
            CouponMutationStatus.ConcurrencyConflict,
            "O cupom foi alterado por outra pessoa. Recarregue e tente novamente.");

    private static CouponMutationResult Invalid(Exception exception) =>
        CouponMutationResult.Failure(
            CouponMutationStatus.Invalid,
            exception.Message);

    private static CouponReservationResult ReservationFailure(
        CouponMutationStatus status,
        string message) => CouponReservationResult.Failure(status, message);
}
