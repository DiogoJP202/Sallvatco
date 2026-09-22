using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sallvat.Application.Carts;
using Sallvat.Application.Payments;
using Sallvat.Application.Time;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Payments;
using Sallvat.Infrastructure.Payments;
using Sallvat.Infrastructure.Persistence;

namespace Sallvat.IntegrationTests.Payments;

public sealed class PostgreSqlPaymentTests
{
    [PostgreSqlFact]
    public async Task RealDatabaseEnforcesPreparationUniquenessAndOptimisticConcurrency()
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SALLVAT_TEST_POSTGRES"))
        {
            Database = "sallvat_payment_tests_" + Guid.NewGuid().ToString("N"),
            Pooling = false,
        };
        var databaseName = builder.Database;
        var options = new DbContextOptionsBuilder<SallvatDbContext>().UseNpgsql(builder.ConnectionString).Options;
        await using var setup = new SallvatDbContext(options);
        try
        {
            await setup.Database.MigrateAsync();
            await PaymentPreparationTests.SeedAsync(setup);
            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
            {
                await using var db = new SallvatDbContext(options);
                return await new PaymentPreparationService(db, new FixedClock()).PrepareAsync(
                    1_000, CartOwner.ForGuest(new string('A', 43)), PaymentEnvironment.Sandbox);
            }));
            Assert.Single(results, result => result.Status == PaymentPreparationStatus.Prepared);
            Assert.All(results, result => Assert.Contains(result.Status, new[]
            {
                PaymentPreparationStatus.Prepared, PaymentPreparationStatus.AlreadyPrepared, PaymentPreparationStatus.Conflict,
            }));

            setup.ChangeTracker.Clear();
            var first = await setup.Payments.SingleAsync();
            var order = await setup.Orders.SingleAsync();
            Assert.Equal(OrderStatus.PendingPayment, order.Status);
            var replay = await new PaymentPreparationService(setup, new FixedClock()).PrepareAsync(
                1_000, CartOwner.ForGuest(new string('A', 43)), PaymentEnvironment.Sandbox);
            Assert.Equal(PaymentPreparationStatus.AlreadyPrepared, replay.Status);
            Assert.Equal(first.Id, replay.PaymentId);
            var variant = await setup.ProductVariants.SingleAsync();
            Assert.Equal(4, variant.OnHand);
            Assert.Equal(2, variant.Reserved);

            await AssertUniqueAsync(options, new Payment(order, PaymentEnvironment.Sandbox, Guid.NewGuid(), FixedClock.Now), "ux_payment_unresolved_order");
            var secondOrder = new Order(1_001, "SVT-20260921-00001001", Guid.NewGuid(), order.SourceCartId, order.CustomerId,
                "Cliente", "cliente@example.com", "11999998888", 140m, 0m, 18.50m, "BRL", null, null,
                "Melhor Envio", "Transportadora", "Expresso", "quote-124", 2, 4, FixedClock.Now, FixedClock.Now, FixedClock.Now.AddMinutes(30));
            setup.Orders.Add(secondOrder);
            await setup.SaveChangesAsync();
            await AssertUniqueAsync(options, new Payment(secondOrder, PaymentEnvironment.Sandbox, first.IdempotencyKey, FixedClock.Now), "ux_payment_idempotency");

            first.RegisterPreference("123-preference", FixedClock.Now);
            await setup.SaveChangesAsync();
            var duplicatePreference = new Payment(secondOrder, PaymentEnvironment.Sandbox, Guid.NewGuid(), FixedClock.Now);
            duplicatePreference.RegisterPreference("123-preference", FixedClock.Now);
            await AssertUniqueAsync(options, duplicatePreference, "ux_payment_preference");

            await using var staleContext = new SallvatDbContext(options);
            var stale = await staleContext.Payments.SingleAsync();
            first.MarkOutcomeUnknown(FixedClock.Now);
            await setup.SaveChangesAsync();
            stale.MarkOutcomeUnknown(FixedClock.Now);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleContext.SaveChangesAsync());
            var invalidAmount = await Assert.ThrowsAsync<PostgresException>(() => setup.Database.ExecuteSqlRawAsync("UPDATE payment SET amount = -1"));
            Assert.Equal(PostgresErrorCodes.CheckViolation, invalidAmount.SqlState);
            Assert.Equal("ck_payment_amount", invalidAmount.ConstraintName);
            Assert.Equal(158.50m, await setup.Payments.AsNoTracking().Select(payment => payment.Amount).SingleAsync());
            Assert.False(setup.Database.HasPendingModelChanges());
        }
        finally
        {
            // Only the isolated database name generated above can be removed; never the supplied database.
            if (databaseName is not null && databaseName.StartsWith("sallvat_payment_tests_", StringComparison.Ordinal)
                && setup.Database.GetDbConnection().Database == databaseName)
            {
                await setup.Database.EnsureDeletedAsync();
            }
        }
    }

    private static async Task AssertUniqueAsync(DbContextOptions<SallvatDbContext> options, Payment payment, string constraint)
    {
        await using var db = new SallvatDbContext(options);
        db.Payments.Add(payment);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        Assert.Equal(constraint, postgres.ConstraintName);
    }

    private sealed class FixedClock : IClock
    {
        internal static readonly DateTimeOffset Now = new(2026, 9, 21, 18, 0, 0, TimeSpan.Zero);
        public DateTimeOffset UtcNow => Now;
    }
}

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SALLVAT_TEST_POSTGRES")))
        {
            Skip = "Requires isolated PostgreSQL through SALLVAT_TEST_POSTGRES (configured in CI).";
        }
    }
}
