using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Payments;
using Sallvat.Infrastructure.Persistence;
using Sallvat.IntegrationTests.Web;

namespace Sallvat.IntegrationTests.Persistence;

public sealed class PaymentPersistenceTests
{
    [Fact]
    public async Task PostgreSqlModelProtectsIdempotencyPreferencesAndUnresolvedOrders()
    {
        await using var application = new SallvatWebApplicationFactory();
        using var scope = application.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var model = context.Model.FindEntityType(typeof(Payment))!;
        var keyIndex = Assert.Single(model.GetIndexes(), index => index.GetDatabaseName() == "ux_payment_idempotency");
        Assert.True(keyIndex.IsUnique);
        Assert.Equal(["Provider", "Environment", "IdempotencyKey"], keyIndex.Properties.Select(property => property.Name));
        var preferenceIndex = Assert.Single(model.GetIndexes(), index => index.GetDatabaseName() == "ux_payment_preference");
        Assert.True(preferenceIndex.IsUnique);
        Assert.Equal("preference_id IS NOT NULL", preferenceIndex.GetFilter());
        var unresolvedIndex = Assert.Single(model.GetIndexes(), index => index.GetDatabaseName() == "ux_payment_unresolved_order");
        Assert.True(unresolvedIndex.IsUnique);
        Assert.Equal("status IN ('Created', 'Pending', 'Approved', 'RequiresAttention')", unresolvedIndex.GetFilter());
        Assert.Equal("OrderId", Assert.Single(unresolvedIndex.Properties).Name);
        Assert.True(model.FindProperty(nameof(Payment.ConcurrencyVersion))!.IsConcurrencyToken);
        Assert.Equal(18, model.FindProperty(nameof(Payment.Amount))!.GetPrecision());
        Assert.Equal(2, model.FindProperty(nameof(Payment.Amount))!.GetScale());
        Assert.Equal(DeleteBehavior.Restrict, Assert.Single(model.GetForeignKeys()).DeleteBehavior);
        var sql = context.Database.GenerateCreateScript();
        Assert.Contains("CONSTRAINT ck_payment_attention CHECK", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX ux_payment_unresolved_order ON payment (order_id)", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PersistsSnapshotAndRejectsStaleUpdateWithoutAffectingOrder()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        using var firstScope = application.Services.CreateScope();
        using var secondScope = application.Services.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var second = secondScope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var now = new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
        var order = new Order(
            1_000, "SVT-20260921-00001000", Guid.NewGuid(), Guid.NewGuid(), null,
            "Cliente Teste", "cliente@example.com", "11999998888",
            140m, 0m, 18.50m, "BRL", null, null,
            "Melhor Envio", "Transportadora Teste", "Expresso", "quote-123", 2, 4,
            now.AddMinutes(-1), now, now.AddMinutes(30));
        var payment = new Payment(order, PaymentEnvironment.Sandbox, Guid.NewGuid(), now);
        first.Orders.Add(order);
        first.Payments.Add(payment);
        await first.SaveChangesAsync();
        var stalePayment = await second.Payments.SingleAsync();
        payment.RegisterPreference("preference-123", now.AddSeconds(1));
        await first.SaveChangesAsync();
        stalePayment.MarkOutcomeUnknown(now.AddSeconds(2));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        first.ChangeTracker.Clear();
        var persisted = await first.Payments.SingleAsync();
        Assert.Equal(payment.IdempotencyKey, persisted.IdempotencyKey);
        Assert.Equal(158.50m, persisted.Amount);
        Assert.Equal("BRL", persisted.Currency);
        Assert.Equal("preference-123", persisted.PreferenceId);
        Assert.Equal(PaymentStatus.Pending, persisted.Status);
        Assert.Equal(OrderStatus.PendingPayment, (await first.Orders.SingleAsync()).Status);
    }
}
