using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sallvat.Application.Payments;
using Sallvat.Application.Time;
using Sallvat.Infrastructure.Persistence;

namespace Sallvat.Infrastructure.Payments;

internal sealed class PaymentRecoveryBatchService(SallvatDbContext db, IPaymentGateway gateway,
    IOptions<MercadoPagoOptions> options, IClock clock) : IPaymentRecoveryBatchService
{
    public async Task<int> RunAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (batchSize is < 1 or > 20) { throw new ArgumentOutOfRangeException(nameof(batchSize)); }
        if (!options.Value.AutomaticRecoveryEnabled || !MercadoPagoOptions.IsValid(options.Value)) { return 0; }
        if (db.ChangeTracker.HasChanges()) { throw new InvalidOperationException("Recovery worker requires a clean context."); }
        var candidates = await PaymentRecoverySchedule.Due(db, clock.UtcNow).AsNoTracking()
            .OrderBy(p => p.DispatchStartedAtUtc).ThenBy(p => p.Id)
            .Select(p => new { p.Id, p.ConcurrencyVersion }).Take(batchSize).ToListAsync(cancellationToken);
        var service = new PaymentRecoveryService(db, gateway, options, clock);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await service.RecoverAutomaticallyAsync(candidate.Id, candidate.ConcurrencyVersion, cancellationToken);
        }
        return candidates.Count;
    }
}
