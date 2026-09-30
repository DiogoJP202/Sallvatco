using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sallvat.Application.Carts;
using Sallvat.Application.Checkout;
using Sallvat.Application.Orders;
using Sallvat.Application.Payments;
using Sallvat.Application.Time;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Payments;
using Sallvat.Infrastructure.Orders;
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
            var adminQuery = new AdminPaymentQuery(setup, Microsoft.Extensions.Options.Options.Create(new MercadoPagoOptions()), new PaymentDispatchTests.Clock());
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
            var capturedDetail = Assert.IsType<AdminPaymentDetails>(await new AdminPaymentQuery(db, Microsoft.Extensions.Options.Options.Create(new MercadoPagoOptions()), new PaymentDispatchTests.Clock()).FindAsync((await db.Payments.SingleAsync()).Id));
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
            Assert.Equal(PaymentRecoveryExecutionState.Running, (await db.PaymentRecoveryExecutions.SingleAsync()).State);
            Assert.Equal(PaymentRecoveryResult.Busy,
                await PaymentRecoveryTests.Service(db, new()).RecoverAsync(payment.Id, version, PaymentRecoveryTests.Operation));
            var clock = new PaymentDispatchTests.Clock { UtcNow = PaymentDispatchTests.Now.AddMinutes(3) };

            // Race independent contexts/connections, not just a process-local lock.
            var recoveries = Enumerable.Range(0, 5).Select(async _ =>
            {
                await using var context = new SallvatDbContext(options);
                return await PaymentRecoveryTests.Service(context, new(), clock).RecoverAsync(payment.Id, version, PaymentRecoveryTests.Operation);
            }).ToArray();
            var webhooks = Enumerable.Range(0, 5).Select(async _ =>
            {
                await using var context = new SallvatDbContext(options);
                return await PaymentWebhookTests.Service(context, new(), clock).HandleAsync(PaymentWebhookTests.Request(now: clock.UtcNow));
            }).ToArray();
            var recovered = await Task.WhenAll(recoveries);
            var notified = await Task.WhenAll(webhooks);
            Assert.All(recovered, result => Assert.Contains(result, new[] { PaymentRecoveryResult.Confirmed,
                PaymentRecoveryResult.Conflict, PaymentRecoveryResult.NotEligible, PaymentRecoveryResult.Unavailable, PaymentRecoveryResult.Busy }));
            Assert.All(notified, result => Assert.Contains(result, new[] { PaymentWebhookResult.Accepted, PaymentWebhookResult.Retry }));
            await PaymentWebhookTests.AssertConfirmedAsync(db);
            var confirmedAudits = await db.AuditLogs.AsNoTracking().Where(a => a.Action == "payment.recovery.completed").ToListAsync();
            var captures = confirmedAudits.Count(a => a.ChangesJson.Contains("\"Confirmed\"", StringComparison.Ordinal))
                + await db.WebhookEvents.CountAsync(e => e.Outcome == WebhookOutcome.Confirmed);
            Assert.Equal(1, captures);
            var history = Assert.IsType<AdminPaymentDetails>(await new AdminPaymentQuery(db,
                Microsoft.Extensions.Options.Options.Create(new MercadoPagoOptions()), new PaymentDispatchTests.Clock()).FindAsync(payment.Id));
            Assert.NotEmpty(history.RecoveryEntries);
            Assert.All(history.RecoveryEntries, entry => Assert.Equal(PaymentRecoveryReason.MissingNotification, entry.Reason));
            Assert.False(history.CanRecover);
            // Subsequent webhook and recovery cannot repeat the financial transition.
            Assert.Equal(PaymentWebhookResult.Accepted, await PaymentWebhookTests.Service(db, new(), clock).HandleAsync(PaymentWebhookTests.Request("after-recovery", now: clock.UtcNow)));
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

    [PostgreSqlFact]
    public async Task RecoveryOwnershipBlocksParallelGetFencesOldResponseAndPreservesHistory()
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
            var clock = new PaymentDispatchTests.Clock();
            var gateway = new PaymentWebhookTests.Gateway
            {
                DuringQuery = async () =>
                {
                    var blocked = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
                    {
                        await using var other = new SallvatDbContext(options);
                        var otherGateway = new PaymentWebhookTests.Gateway();
                        var result = await PaymentRecoveryTests.Service(other, otherGateway, clock)
                            .RecoverAsync(payment.Id, payment.ConcurrencyVersion, PaymentRecoveryTests.Operation);
                        Assert.Equal(0, otherGateway.Calls);
                        return result;
                    }));
                    Assert.All(blocked, result => Assert.Equal(PaymentRecoveryResult.Busy, result));
                    await using var check = new SallvatDbContext(options);
                    check.PaymentRecoveryExecutions.Add(new(Guid.NewGuid(), payment.Id, clock.UtcNow));
                    var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => check.SaveChangesAsync());
                    Assert.Equal("ux_recovery_execution_running", Assert.IsType<PostgresException>(duplicate.InnerException).ConstraintName);
                    check.ChangeTracker.Clear();
                    var invalid = await Assert.ThrowsAsync<PostgresException>(() => check.Database.ExecuteSqlRawAsync(
                        "UPDATE payment_recovery_execution SET state = 'Completed', finished_at_utc = NULL"));
                    Assert.Equal("ck_recovery_execution_state", invalid.ConstraintName);
                    clock.UtcNow += PaymentRecoveryExecution.Lifetime;
                    var pending = new PaymentWebhookTests.Gateway
                    {
                        Result = new(PaymentOrderQueryStatus.Found,
                        PaymentWebhookTests.Observation() with { State = ObservedOrderState.Created, PaidAmount = 0, HasTransactions = false, SettledPaymentId = null })
                    };
                    Assert.Equal(PaymentRecoveryResult.Observed, await PaymentRecoveryTests.Service(check, pending, clock)
                        .RecoverAsync(payment.Id, payment.ConcurrencyVersion, PaymentRecoveryTests.Operation));
                },
            };
            Assert.Equal(PaymentRecoveryResult.Interrupted, await PaymentRecoveryTests.Service(db, gateway, clock)
                .RecoverAsync(payment.Id, payment.ConcurrencyVersion, PaymentRecoveryTests.Operation));
            Assert.Empty(await db.InventoryMovements.ToListAsync());
            Assert.Equal(PaymentStatus.Pending, (await db.Payments.SingleAsync()).Status);
            var executions = await db.PaymentRecoveryExecutions.OrderBy(e => e.StartedAtUtc).ToListAsync();
            Assert.Equal(2, executions.Count);
            Assert.Equal(PaymentRecoveryExecutionState.Interrupted, executions[0].State);
            Assert.Equal(PaymentRecoveryExecutionState.Completed, executions[1].State);
            var history = Assert.IsType<AdminPaymentDetails>(await new AdminPaymentQuery(db,
                Microsoft.Extensions.Options.Options.Create(PaymentRecoveryTests.Configuration()), clock).FindAsync(payment.Id));
            Assert.True(history.CanRecover);
            Assert.Null(history.RecoveryBlockedUntilUtc);
            Assert.Equal(2, history.Executions.Count);
            Assert.Contains(history.RecoveryEntries, entry => entry.RequestId == executions[0].Id && entry.Result == PaymentRecoveryResult.Interrupted);
            var downgrade = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>()
                .MigrateAsync("20260928110452_AddPaymentWebhookConfirmation"));
            Assert.Equal(PostgresErrorCodes.RaiseException, downgrade.SqlState);
            Assert.Equal(2, await db.PaymentRecoveryExecutions.CountAsync());
            Assert.Equal(PaymentRecoveryResult.Confirmed, await PaymentRecoveryTests.Service(db, new(), clock)
                .RecoverAsync(payment.Id, payment.ConcurrencyVersion, PaymentRecoveryTests.Operation));
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
    public async Task AutomaticRecoveryPreservesLegacyHistoryRollsBackOutcomeFailureAndRacesSafely()
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
            await db.GetService<IMigrator>().MigrateAsync("20260929113017_AddPaymentRecoveryExecutions");
            await PaymentWebhookTests.SeedAsync(db);
            var payment = await db.Payments.SingleAsync();
            var legacyId = Guid.NewGuid();
            var now = PaymentDispatchTests.Now;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO payment_recovery_execution (id, payment_id, state, started_at_utc, expires_at_utc, finished_at_utc, concurrency_version)
                VALUES ({legacyId}, {payment.Id}, 'Completed', {now}, {now.AddMinutes(2)}, {now}, {Guid.NewGuid()})
                """);
            await db.Database.MigrateAsync();
            var legacy = await db.PaymentRecoveryExecutions.SingleAsync();
            Assert.Equal(PaymentRecoverySource.Manual, legacy.Source);
            Assert.Null(legacy.Outcome);
            var clock = new PaymentDispatchTests.Clock { UtcNow = now.AddMinutes(2) };
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION fail_test_auto_outcome() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'simulated outcome failure' USING ERRCODE = '23514'; END $$;
                CREATE TRIGGER fail_auto_outcome BEFORE UPDATE ON payment_recovery_execution FOR EACH ROW
                WHEN (NEW.source = 'Automatic' AND NEW.state = 'Completed') EXECUTE FUNCTION fail_test_auto_outcome();
                """);
            await AutomaticPaymentRecoveryTests.Service(db, new(), clock).RunAsync(20);
            db.ChangeTracker.Clear();
            Assert.Equal(PaymentStatus.Pending, (await db.Payments.SingleAsync()).Status);
            Assert.Equal(OrderStatus.PendingPayment, (await db.Orders.SingleAsync()).Status);
            Assert.Empty(await db.InventoryMovements.ToListAsync());
            var failed = await db.PaymentRecoveryExecutions.SingleAsync(e => e.Source == PaymentRecoverySource.Automatic);
            Assert.Equal(PaymentRecoveryExecutionState.Running, failed.State);
            Assert.Null(failed.Outcome);
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_auto_outcome ON payment_recovery_execution; DROP FUNCTION fail_test_auto_outcome();");
            clock.UtcNow = now.AddMinutes(4);
            var blocked = new PaymentWebhookTests.Gateway();
            Assert.Equal(0, await AutomaticPaymentRecoveryTests.Service(db, blocked, clock).RunAsync(20));
            Assert.Equal(0, blocked.Calls);
            clock.UtcNow = now.AddMinutes(7);
            var gateway = new PaymentWebhookTests.Gateway();
            await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
            {
                await using var other = new SallvatDbContext(options);
                await AutomaticPaymentRecoveryTests.Service(other, gateway, clock).RunAsync(20);
            }));
            Assert.Equal(1, gateway.Calls);
            await PaymentWebhookTests.AssertConfirmedAsync(db);
            Assert.Equal(2, await db.PaymentRecoveryExecutions.CountAsync(e => e.Source == PaymentRecoverySource.Automatic));
            Assert.Equal(1, await db.PaymentRecoveryExecutions.CountAsync(e => e.Outcome == PaymentRecoveryOutcome.Confirmed));
            Assert.Equal(1, await db.PaymentRecoveryExecutions.CountAsync(e => e.Outcome == PaymentRecoveryOutcome.Interrupted));
            Assert.Empty(await db.AuditLogs.ToListAsync());
            Assert.Empty(await db.Users.ToListAsync());
            Assert.Equal(PaymentWebhookResult.Accepted, await PaymentWebhookTests.Service(db, new(), clock)
                .HandleAsync(PaymentWebhookTests.Request(now: clock.UtcNow)));
            await PaymentWebhookTests.AssertConfirmedAsync(db);
            var invalid = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "UPDATE payment_recovery_execution SET outcome = NULL WHERE source = 'Automatic' AND state = 'Completed'"));
            Assert.Equal("ck_recovery_execution_outcome", invalid.ConstraintName);
            var downgrade = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>()
                .MigrateAsync("20260929113017_AddPaymentRecoveryExecutions"));
            Assert.Equal(PostgresErrorCodes.RaiseException, downgrade.SqlState);
            Assert.Equal(2, await db.PaymentRecoveryExecutions.CountAsync(e => e.Source == PaymentRecoverySource.Automatic));
            // Later empty tables may already have been removed before an older migration's guard fails.
            // Restore the current schema before exercising the current application's read model.
            await db.Database.MigrateAsync();
            var detail = Assert.IsType<AdminPaymentDetails>(await new AdminPaymentQuery(db,
                Microsoft.Extensions.Options.Options.Create(AutomaticPaymentRecoveryTests.Configuration()), clock).FindAsync(payment.Id));
            Assert.Equal(2, detail.AutomaticRecoveryCount);
            Assert.True(detail.AutomaticRecoveryEnabled);
            Assert.Equal(3, detail.Executions.Count);
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
    public async Task FollowUpQueueUsesDurableLimitsAndLeaseBoundariesWithoutChangingPayment()
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
            var payment = await db.Payments.SingleAsync();
            var version = payment.ConcurrencyVersion;
            var clock = new PaymentDispatchTests.Clock { UtcNow = PaymentDispatchTests.Now.AddHours(24) };
            var query = new AdminPaymentQuery(db, Microsoft.Extensions.Options.Options.Create(new MercadoPagoOptions()), clock);
            Assert.Empty((await query.ListAsync(AdminPaymentFilter.RecoveryFollowUp)).Items);
            clock.UtcNow = clock.UtcNow.AddMilliseconds(1);
            Assert.Equal(AdminRecoveryFollowUp.WindowExpired, Assert.Single((await query.ListAsync(AdminPaymentFilter.RecoveryFollowUp)).Items).RecoveryFollowUp);
            Assert.Single((await query.ListAsync(AdminPaymentFilter.Attention)).Items);
            for (var index = 0; index < 3; index++)
            {
                var execution = new PaymentRecoveryExecution(Guid.NewGuid(), payment.Id, PaymentDispatchTests.Now.AddMinutes(index), PaymentRecoverySource.Automatic);
                execution.Complete(PaymentDispatchTests.Now.AddMinutes(index), PaymentRecoveryOutcome.Unavailable);
                db.PaymentRecoveryExecutions.Add(execution);
            }
            await db.SaveChangesAsync();
            var combined = AdminRecoveryFollowUp.AttemptsExhausted | AdminRecoveryFollowUp.WindowExpired;
            Assert.Equal(combined, Assert.Single((await query.ListAsync(AdminPaymentFilter.RecoveryFollowUp)).Items).RecoveryFollowUp);
            Assert.Empty((await query.ListAsync(AdminPaymentFilter.RecoveryFollowUp, payment.Id)).Items);
            db.PaymentRecoveryExecutions.Add(new(Guid.NewGuid(), payment.Id, clock.UtcNow));
            await db.SaveChangesAsync();
            Assert.Empty((await query.ListAsync(AdminPaymentFilter.RecoveryFollowUp)).Items);
            clock.UtcNow += PaymentRecoveryExecution.Lifetime;
            (await db.Orders.SingleAsync()).TransitionTo(OrderStatus.Cancelled, clock.UtcNow);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var detail = Assert.IsType<AdminPaymentDetails>(await query.FindAsync(payment.Id));
            Assert.Equal(combined, detail.Payment.RecoveryFollowUp);
            Assert.Equal(OrderStatus.Cancelled, detail.Payment.OrderStatus);
            Assert.False(detail.CanRecover);
            Assert.Single((await query.ListAsync(AdminPaymentFilter.Attention)).Items);
            Assert.Empty(db.ChangeTracker.Entries());
            Assert.Equal(version, (await db.Payments.AsNoTracking().SingleAsync()).ConcurrencyVersion);
            Assert.Empty(await db.AuditLogs.ToListAsync());
            Assert.Empty(await db.InventoryMovements.ToListAsync());
            Assert.Equal(4, await db.PaymentRecoveryExecutions.CountAsync());
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
    public async Task ReviewedOrderChecksTermsAndConcurrentSubmissionsReserveOnlyOnce()
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
            var now = FixedClock.Now;
            var owner = CartOwner.ForGuest(new string('A', 43));
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(owner.GuestToken!)));
            var cart = Sallvat.Domain.Carts.Cart.CreateGuest(hash, now, now.AddHours(2));
            var product = new Sallvat.Domain.Catalog.Product("Perfume", "reviewed-perfume", "Resumo", "Descrição", "Família",
                "Saída", "Coração", "Fundo", "EDP", null, null, null, null, null, now);
            product.Publish(true, true, now);
            db.Products.Add(product);
            db.Carts.Add(cart);
            await db.SaveChangesAsync();
            var variant = new Sallvat.Domain.Catalog.ProductVariant(product.Id, "REVIEW-50", 50, 70m, .13m, 5m, 16m, 11m, now);
            variant.AdjustOnHand(4, now);
            db.ProductVariants.Add(variant);
            await db.SaveChangesAsync();
            db.CartItems.Add(new(cart.Id, variant.Id, 1, 70m, now));
            await db.SaveChangesAsync();
            var expected = new OrderReviewExpectation([new(variant.Id, 1, 70m, "BRL")], 70m, 0m, null);
            var request = new CreateOrderRequest(Guid.NewGuid(), owner,
                new CheckoutDraftInput(new("Cliente Teste", "cliente@example.com", "11999998888"),
                    new(null, "Cliente Teste", "01310100", "Avenida Paulista", "1000", null, "Bela Vista", "São Paulo", "SP")),
                new("Melhor Envio", "Correios", "SEDEX", "quote-review", 18.50m, "BRL", 2, 4, now, now.AddMinutes(10)), expected);
            var orderOptions = Microsoft.Extensions.Options.Options.Create(new OrderOptions());
            foreach (var changed in new[]
            {
                expected with { ItemsSubtotal = 69m }, expected with { DiscountTotal = 1m },
                expected with { CouponId = 123 }, expected with { Lines = [new(variant.Id, 2, 70m, "BRL")] },
                expected with { Lines = [new(variant.Id, 1, 69m, "BRL")] },
            })
            {
                await using var isolated = new SallvatDbContext(options);
                Assert.Equal(OrderCreationStatus.Invalid, (await new OrderService(isolated, new FixedClock(), orderOptions)
                    .CreateAsync(request with { ExpectedReview = changed })).Status);
            }
            Assert.Empty(await db.Orders.ToListAsync());
            Assert.Empty(await db.StockReservations.ToListAsync());
            var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
            {
                await using var isolated = new SallvatDbContext(options);
                return await new OrderService(isolated, new FixedClock(), orderOptions).CreateAsync(request);
            }));
            Assert.Contains(results, result => result.Succeeded);
            Assert.All(results, result => Assert.Contains(result.Status,
                new[] { OrderCreationStatus.Succeeded, OrderCreationStatus.ConcurrencyConflict }));
            db.ChangeTracker.Clear();
            Assert.Equal(88.50m, (await db.Orders.SingleAsync()).GrandTotal);
            Assert.Single(await db.OrderItems.ToListAsync());
            Assert.Single(await db.StockReservations.ToListAsync());
            Assert.Equal(1, (await db.ProductVariants.SingleAsync()).Reserved);
            Assert.Equal(4, (await db.ProductVariants.SingleAsync()).OnHand);
            Assert.Single(await db.InventoryMovements.ToListAsync());
            Assert.Single(await db.Customers.ToListAsync());
            Assert.Empty(await db.CartItems.ToListAsync());
            Assert.True((await new OrderService(db, new FixedClock(), orderOptions).CreateAsync(request)).WasAlreadyCreated);
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
    public async Task RefundIntentIsAtomicUniqueQueryableAndCannotBeDroppedWithHistory()
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
            var (payment, order) = await PaymentRefundPreparationTests.SeedAsync(db);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION fail_test_refund_audit() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'simulated audit failure' USING ERRCODE='23514'; END $$;
                CREATE TRIGGER fail_refund_audit BEFORE INSERT ON audit_log FOR EACH ROW
                    WHEN (NEW.action = 'payment.refund.prepared') EXECUTE FUNCTION fail_test_refund_audit();
                """);
            Assert.Equal(PaymentRefundPreparationStatus.Unavailable, (await PaymentRefundPreparationTests.Service(db)
                .PrepareAsync(payment.Id, payment.ConcurrencyVersion, order.ConcurrencyVersion, PaymentRefundPreparationTests.Operation)).Status);
            Assert.Empty(await db.PaymentRefundRequests.ToListAsync());
            Assert.Empty(await db.AuditLogs.ToListAsync());
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_refund_audit ON audit_log; DROP FUNCTION fail_test_refund_audit();");
            var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
            {
                await using var isolated = new SallvatDbContext(options);
                return await PaymentRefundPreparationTests.Service(isolated).PrepareAsync(payment.Id,
                    payment.ConcurrencyVersion, order.ConcurrencyVersion, PaymentRefundPreparationTests.Operation);
            }));
            Assert.Single(results, r => r.Status == PaymentRefundPreparationStatus.Prepared);
            Assert.All(results, r => Assert.Contains(r.Status, new[] { PaymentRefundPreparationStatus.Prepared,
                PaymentRefundPreparationStatus.AlreadyPrepared, PaymentRefundPreparationStatus.Conflict }));
            Assert.Single(await db.PaymentRefundRequests.ToListAsync());
            Assert.Single(await db.AuditLogs.ToListAsync());
            var config = Microsoft.Extensions.Options.Options.Create(PaymentRefundPreparationTests.Configuration());
            var query = new AdminPaymentQuery(db, config, new FixedClock());
            Assert.True((await query.ListAsync(AdminPaymentFilter.RefundPrepared)).Items.Single().HasPreparedRefund);
            Assert.NotNull((await query.FindAsync(payment.Id))!.RefundRequest);
            await using (var duplicate = new SallvatDbContext(options))
            {
                duplicate.PaymentRefundRequests.Add(new(Guid.NewGuid(), payment, order, PaymentRecoveryTests.AdminId,
                    PaymentRefundReason.OperationalCorrection, FixedClock.Now));
                var error = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
                Assert.Equal("ux_refund_request_payment", Assert.IsType<PostgresException>(error.InnerException).ConstraintName);
            }
            var constraint = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "UPDATE payment_refund_request SET amount = 0"));
            Assert.Equal("ck_refund_request_amount", constraint.ConstraintName);
            var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
            Assert.EndsWith("AddPaymentRefundRequests", migrations[^1]);
            var rollback = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(migrations[^2]));
            Assert.Contains("Cannot remove durable refund intentions", rollback.MessageText);
            Assert.Single(await db.PaymentRefundRequests.ToListAsync());
            Assert.Contains(migrations[^1], await db.Database.GetAppliedMigrationsAsync());
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
