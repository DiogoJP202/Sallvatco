using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sallvat.Application.Carts;
using Sallvat.Application.Payments;
using Sallvat.Application.Time;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Payments;
using Sallvat.Infrastructure.Payments;
using Sallvat.Infrastructure.Persistence;
using Sallvat.IntegrationTests.Web;

namespace Sallvat.IntegrationTests.Payments;

public sealed class PostgreSqlPaymentTests
{
    [Fact]
    public async Task IsolatedDatabaseOptionsPreserveTheApplicationModel()
    {
        await using var application = new SallvatWebApplicationFactory();
        using var scope = application.Services.CreateScope();
        await using var db = new SallvatDbContext(CreateOptions(scope.ServiceProvider,
            "Host=127.0.0.1;Port=1;Database=sallvat_model_test;Username=test;Password=test"));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [PostgreSqlFact]
    public async Task RealDatabaseEnforcesPreparationUniquenessAndOptimisticConcurrency()
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SALLVAT_TEST_POSTGRES"))
        {
            Database = "sallvat_payment_tests_" + Guid.NewGuid().ToString("N"),
            Pooling = false,
        };
        var databaseName = builder.Database;
        await using var application = new SallvatWebApplicationFactory();
        using var scope = application.Services.CreateScope();
        var options = CreateOptions(scope.ServiceProvider, builder.ConnectionString);
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
            var adminQuery = new AdminPaymentQuery(setup, Microsoft.Extensions.Options.Options.Create(new MercadoPagoOptions()));
            Assert.Single((await adminQuery.ListAsync(AdminPaymentFilter.Attention)).Items);
            Assert.Empty((await adminQuery.ListAsync(AdminPaymentFilter.Pending)).Items);
            Assert.Empty((await adminQuery.ListAsync(AdminPaymentFilter.Approved)).Items);
            Assert.Empty((await adminQuery.ListAsync(AdminPaymentFilter.All, first.Id)).Items);
            var adminDetail = Assert.IsType<AdminPaymentDetails>(await adminQuery.FindAsync(first.Id));
            Assert.Equal(PaymentAttentionReason.PreferenceOutcomeUnknown, adminDetail.Payment.AttentionReason);
            Assert.Empty(adminDetail.Receipts);
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

    private static DbContextOptions<SallvatDbContext> CreateOptions(IServiceProvider services, string connectionString) =>
        // Preserve Identity schema options and migration history from the actual application.
        new DbContextOptionsBuilder<SallvatDbContext>(services.GetRequiredService<DbContextOptions<SallvatDbContext>>())
            .UseNpgsql(connectionString).Options;

    [PostgreSqlFact]
    public async Task RealDatabaseAllowsOnlyOneExternalDispatchAndEnforcesOrderIdUniqueness()
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SALLVAT_TEST_POSTGRES"))
        {
            Database = "sallvat_payment_tests_" + Guid.NewGuid().ToString("N"),
            Pooling = false,
        };
        await using var app = new SallvatWebApplicationFactory();
        using var scope = app.Services.CreateScope();
        var options = CreateOptions(scope.ServiceProvider, builder.ConnectionString);
        await using var db = new SallvatDbContext(options);
        try
        {
            await db.Database.MigrateAsync();
            await PaymentPreparationTests.SeedAsync(db);
            var prepared = await new PaymentPreparationService(db, new FixedClock()).PrepareAsync(1_000, PaymentDispatchTests.Owner, PaymentEnvironment.Sandbox);
            var id = prepared.PaymentId!.Value;
            var gateway = new PaymentDispatchTests.Gateway();
            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
            {
                await using var context = new SallvatDbContext(options);
                return await new PaymentDispatchService(context, gateway,
                    Microsoft.Extensions.Options.Options.Create(PaymentDispatchTests.Configuration()), new FixedClock())
                    .DispatchAsync(id, PaymentDispatchTests.Owner);
            }));
            Assert.Equal(1, gateway.Calls);
            Assert.Contains(results, r => r.Status == PaymentDispatchStatus.Ready);
            Assert.All(results, r => Assert.Contains(r.Status,
                new[] { PaymentDispatchStatus.Ready, PaymentDispatchStatus.Conflict, PaymentDispatchStatus.RequiresAttention }));
            db.ChangeTracker.Clear();
            var payment = await db.Payments.SingleAsync();
            Assert.Equal(PaymentDispatchState.Completed, payment.DispatchState);
            Assert.Equal("ORD-dispatch", payment.ExternalOrderId);
            var order = await db.Orders.SingleAsync();
            var second = new Order(1_001, "SVT-20260921-00001001", Guid.NewGuid(), order.SourceCartId, order.CustomerId,
                "Cliente", "cliente@example.com", "11999998888", 140m, 0m, 18.50m, "BRL", null, null,
                "Melhor Envio", "Transportadora", "Expresso", "quote-124", 2, 4, FixedClock.Now, FixedClock.Now, FixedClock.Now.AddMinutes(30));
            db.Orders.Add(second);
            await db.SaveChangesAsync();
            var duplicate = new Payment(second, PaymentEnvironment.Sandbox, Guid.NewGuid(), FixedClock.Now);
            var token = Guid.NewGuid();
            duplicate.TryBeginOrderDispatch(token, FixedClock.Now);
            duplicate.CompleteOrderDispatch(token, "ORD-dispatch", true, FixedClock.Now);
            await AssertUniqueAsync(options, duplicate, "ux_payment_external_order");
            var invalid = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE payment SET dispatch_started_at_utc = NULL"));
            Assert.Equal(PostgresErrorCodes.CheckViolation, invalid.SqlState);
            Assert.Equal("ck_payment_dispatch", invalid.ConstraintName);
            var rollback = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>()
                .MigrateAsync("20260921131953_AddPaymentFoundation"));
            Assert.Equal(PostgresErrorCodes.RaiseException, rollback.SqlState);
            Assert.Equal(PaymentDispatchState.Completed, await db.Payments.AsNoTracking().Select(p => p.DispatchState).SingleAsync());
        }
        finally
        {
            if (builder.Database!.StartsWith("sallvat_payment_tests_", StringComparison.Ordinal)
                && db.Database.GetDbConnection().Database == builder.Database)
            {
                await db.Database.EnsureDeletedAsync();
            }
        }
    }

    [PostgreSqlFact]
    public async Task WebhookRollsBackOnFailureAndConcurrentDeliveriesConfirmExactlyOnce()
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SALLVAT_TEST_POSTGRES"))
        {
            Database = "sallvat_payment_tests_" + Guid.NewGuid().ToString("N"),
            Pooling = false,
        };
        await using var app = new SallvatWebApplicationFactory();
        using var scope = app.Services.CreateScope();
        var options = CreateOptions(scope.ServiceProvider, builder.ConnectionString);
        await using var db = new SallvatDbContext(options);
        try
        {
            await db.Database.MigrateAsync();
            await PaymentWebhookTests.SeedAsync(db);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION fail_test_sale() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'simulated sale failure' USING ERRCODE = '23514'; END $$;
                CREATE TRIGGER fail_sale BEFORE INSERT ON inventory_movement FOR EACH ROW EXECUTE FUNCTION fail_test_sale();
                """);
            var gateway = new PaymentWebhookTests.Gateway();
            Assert.Equal(PaymentWebhookResult.Retry, await PaymentWebhookTests.Service(db, gateway).HandleAsync(PaymentWebhookTests.Request()));
            db.ChangeTracker.Clear();
            Assert.Equal(PaymentStatus.Pending, (await db.Payments.SingleAsync()).Status);
            Assert.Equal(OrderStatus.PendingPayment, (await db.Orders.SingleAsync()).Status);
            Assert.Equal(4, (await db.ProductVariants.SingleAsync()).OnHand);
            Assert.Empty(await db.WebhookEvents.ToListAsync());
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_sale ON inventory_movement; DROP FUNCTION fail_test_sale();");

            var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
            {
                await using var context = new SallvatDbContext(options);
                return await PaymentWebhookTests.Service(context, gateway).HandleAsync(PaymentWebhookTests.Request());
            }));
            Assert.Contains(PaymentWebhookResult.Accepted, results);
            Assert.All(results, result => Assert.True(result is PaymentWebhookResult.Accepted or PaymentWebhookResult.Retry));
            await PaymentWebhookTests.AssertConfirmedAsync(db);
            Assert.Equal(1, await db.WebhookEvents.CountAsync());
            var capturedDetail = Assert.IsType<AdminPaymentDetails>(await new AdminPaymentQuery(db, Microsoft.Extensions.Options.Options.Create(new MercadoPagoOptions())).FindAsync((await db.Payments.SingleAsync()).Id));
            Assert.Equal(PaymentStatus.Approved, capturedDetail.Payment.Status);
            Assert.Equal(WebhookOutcome.Confirmed, Assert.Single(capturedDetail.Receipts).Outcome);
            Assert.Equal("PAY-webhook", capturedDetail.ExternalPaymentId);
            Assert.Equal(PaymentWebhookResult.Accepted, await PaymentWebhookTests.Service(db, gateway).HandleAsync(PaymentWebhookTests.Request("new-receipt")));
            await PaymentWebhookTests.AssertConfirmedAsync(db);
            var duplicate = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("""
                INSERT INTO payment_webhook_event (delivery_key, payment_id, external_order_id, outcome, received_at_utc)
                SELECT delivery_key, payment_id, external_order_id, outcome, received_at_utc FROM payment_webhook_event LIMIT 1
                """));
            Assert.Equal("23505", duplicate.SqlState);
            var invalid = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE payment SET confirmed_at_utc = NULL"));
            Assert.Equal("23514", invalid.SqlState);
            var downgrade = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>()
                .MigrateAsync("20260922130251_AddPaymentOrderDispatch"));
            Assert.Equal("P0001", downgrade.SqlState);
            await PaymentWebhookTests.AssertConfirmedAsync(db);
        }
        finally
        {
            if (builder.Database!.StartsWith("sallvat_payment_tests_", StringComparison.Ordinal)
                && db.Database.GetDbConnection().Database == builder.Database)
            {
                await db.Database.EnsureDeletedAsync();
            }
        }
    }

    [PostgreSqlFact]
    public async Task RecoveryAuditFailureRollsBackCaptureAndRecoveryCompetesSafelyWithWebhook()
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SALLVAT_TEST_POSTGRES"))
        {
            Database = "sallvat_payment_tests_" + Guid.NewGuid().ToString("N"),
            Pooling = false,
        };
        await using var app = new SallvatWebApplicationFactory();
        using var scope = app.Services.CreateScope();
        var options = CreateOptions(scope.ServiceProvider, builder.ConnectionString);
        await using var db = new SallvatDbContext(options);
        try
        {
            await db.Database.MigrateAsync();
            await PaymentWebhookTests.SeedAsync(db);
            await PaymentRecoveryTests.SeedAdminAsync(db);
            var payment = await db.Payments.SingleAsync();
            var version = payment.ConcurrencyVersion;
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION fail_test_recovery_audit() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'simulated audit failure' USING ERRCODE = '23514'; END $$;
                CREATE TRIGGER fail_recovery_audit BEFORE INSERT ON audit_log FOR EACH ROW
                WHEN (NEW.action = 'payment.recovery.completed') EXECUTE FUNCTION fail_test_recovery_audit();
                """);
            Assert.Equal(PaymentRecoveryResult.Unavailable,
                await PaymentRecoveryTests.Service(db, new()).RecoverAsync(payment.Id, version, PaymentRecoveryTests.Operation));
            db.ChangeTracker.Clear();
            Assert.Equal(PaymentStatus.Pending, (await db.Payments.SingleAsync()).Status);
            Assert.Equal(OrderStatus.PendingPayment, (await db.Orders.SingleAsync()).Status);
            Assert.Equal(4, (await db.ProductVariants.SingleAsync()).OnHand);
            Assert.Equal(2, (await db.ProductVariants.SingleAsync()).Reserved);
            Assert.Empty(await db.InventoryMovements.ToListAsync());
            Assert.Empty(await db.WebhookEvents.ToListAsync());
            Assert.Equal("payment.recovery.requested", (await db.AuditLogs.SingleAsync()).Action);
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_recovery_audit ON audit_log; DROP FUNCTION fail_test_recovery_audit();");

            // Race independent contexts/connections, not just a process-local lock.
            var recoveries = Enumerable.Range(0, 5).Select(async _ =>
            {
                await using var context = new SallvatDbContext(options);
                return await PaymentRecoveryTests.Service(context, new()).RecoverAsync(payment.Id, version, PaymentRecoveryTests.Operation);
            }).ToArray();
            var webhooks = Enumerable.Range(0, 5).Select(async _ =>
            {
                await using var context = new SallvatDbContext(options);
                return await PaymentWebhookTests.Service(context, new()).HandleAsync(PaymentWebhookTests.Request());
            }).ToArray();
            var recovered = await Task.WhenAll(recoveries);
            var notified = await Task.WhenAll(webhooks);
            Assert.All(recovered, result => Assert.Contains(result, new[] { PaymentRecoveryResult.Confirmed,
                PaymentRecoveryResult.Conflict, PaymentRecoveryResult.NotEligible, PaymentRecoveryResult.Unavailable }));
            Assert.All(notified, result => Assert.Contains(result, new[] { PaymentWebhookResult.Accepted, PaymentWebhookResult.Retry }));
            await PaymentWebhookTests.AssertConfirmedAsync(db);
            var confirmedAudits = await db.AuditLogs.AsNoTracking().Where(a => a.Action == "payment.recovery.completed").ToListAsync();
            var captures = confirmedAudits.Count(a => a.ChangesJson.Contains("\"Confirmed\"", StringComparison.Ordinal))
                + await db.WebhookEvents.CountAsync(e => e.Outcome == WebhookOutcome.Confirmed);
            Assert.Equal(1, captures);
            var history = Assert.IsType<AdminPaymentDetails>(await new AdminPaymentQuery(db,
                Microsoft.Extensions.Options.Options.Create(new MercadoPagoOptions())).FindAsync(payment.Id));
            Assert.NotEmpty(history.RecoveryEntries);
            Assert.All(history.RecoveryEntries, entry => Assert.Equal(PaymentRecoveryReason.MissingNotification, entry.Reason));
            Assert.False(history.CanRecover);
            // Subsequent webhook and recovery cannot repeat the financial transition.
            Assert.Equal(PaymentWebhookResult.Accepted, await PaymentWebhookTests.Service(db, new()).HandleAsync(PaymentWebhookTests.Request("after-recovery")));
            var latest = await db.Payments.SingleAsync();
            Assert.Equal(PaymentRecoveryResult.NotEligible,
                await PaymentRecoveryTests.Service(db, new()).RecoverAsync(latest.Id, latest.ConcurrencyVersion, PaymentRecoveryTests.Operation));
            await PaymentWebhookTests.AssertConfirmedAsync(db);
        }
        finally
        {
            if (builder.Database!.StartsWith("sallvat_payment_tests_", StringComparison.Ordinal)
                && db.Database.GetDbConnection().Database == builder.Database)
            {
                await db.Database.EnsureDeletedAsync();
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
