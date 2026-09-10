using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Sallvat.Application.Carts;
using Sallvat.Application.Checkout;
using Sallvat.Application.Shipping;
using Sallvat.Application.Time;
using Sallvat.IntegrationTests.Catalog;
using Sallvat.IntegrationTests.Web;

namespace Sallvat.IntegrationTests.Checkout;

public sealed class CheckoutFreightTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 10, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task QuoteUsesCurrentPhysicalItemsAndNormalizesPostalCode()
    {
        var gateway = new FakeFreightService(Quote(18.50m));
        await using var application = new AccountWebApplicationFactory(
            clock: new FixedClock(Now),
            freightService: gateway);
        await application.InitializeDatabaseAsync();
        var product = await PublishedCatalogFixture.CreateAsync(
            application,
            "checkout-freight-quote");
        var owner = CartOwner.ForGuest(Token('Q'));
        await AddItemAsync(application, owner, product.AvailableVariantId, 2);

        using var scope = application.Services.CreateScope();
        var result = await scope.ServiceProvider
            .GetRequiredService<ICheckoutService>()
            .QuoteFreightAsync(owner, "30140-071");

        Assert.True(result.Succeeded);
        var request = Assert.Single(gateway.Requests);
        Assert.Equal("30140071", request.Request.DestinationPostalCode);
        Assert.False(request.ForceRefresh);
        var item = Assert.Single(request.Request.Items);
        Assert.Equal(
            product.AvailableVariantId.ToString(CultureInfo.InvariantCulture),
            item.Reference);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(299.90m, item.UnitPrice);
        Assert.Equal(0.4m, item.WeightKg);
        Assert.Equal(12m, item.HeightCm);
        Assert.Equal(8m, item.WidthCm);
        Assert.Equal(8m, item.LengthCm);
    }

    [Fact]
    public async Task RevalidationRejectsChangedPriceThenBuildsTrustedSnapshot()
    {
        var gateway = new FakeFreightService(Quote(21.90m));
        await using var application = new AccountWebApplicationFactory(
            clock: new FixedClock(Now),
            freightService: gateway);
        await application.InitializeDatabaseAsync();
        var product = await PublishedCatalogFixture.CreateAsync(
            application,
            "checkout-freight-revalidation");
        var owner = CartOwner.ForGuest(Token('R'));
        await AddItemAsync(application, owner, product.AvailableVariantId, 1);

        using var scope = application.Services.CreateScope();
        var service = scope.ServiceProvider
            .GetRequiredService<ICheckoutService>();
        var changed = await service.RevalidateFreightAsync(
            owner,
            "30140-071",
            "melhor-envio:2",
            18.50m);
        var accepted = await service.RevalidateFreightAsync(
            owner,
            "30140-071",
            "melhor-envio:2",
            21.90m);

        Assert.Equal(FreightSelectionStatus.PriceChanged, changed.Status);
        Assert.Equal(21.90m, changed.CurrentOption?.Price);
        Assert.True(accepted.Succeeded);
        Assert.Equal("Melhor Envio", accepted.Snapshot?.Provider);
        Assert.Equal("Correios", accepted.Snapshot?.Carrier);
        Assert.Equal("SEDEX", accepted.Snapshot?.Service);
        Assert.Equal(21.90m, accepted.Snapshot?.Price);
        Assert.All(gateway.Requests, request => Assert.True(
            request.ForceRefresh));
    }

    private static FreightQuoteResult Quote(decimal price) =>
        FreightQuoteResult.Success(
        [
            new FreightQuoteOption(
                "melhor-envio:2",
                "Correios",
                "SEDEX",
                price,
                "BRL",
                2,
                4,
                Now,
                Now.AddMinutes(10)),
        ]);

    private static async Task AddItemAsync(
        AccountWebApplicationFactory application,
        CartOwner owner,
        long variantId,
        int quantity)
    {
        using var scope = application.Services.CreateScope();
        var result = await scope.ServiceProvider
            .GetRequiredService<ICartService>()
            .AddItemAsync(owner, variantId, quantity);
        Assert.True(result.Succeeded);
    }

    private static string Token(char value) => new(value, 43);

    private sealed class FakeFreightService(
        FreightQuoteResult result) : IFreightService
    {
        public List<CapturedRequest> Requests { get; } = [];

        public Task<FreightQuoteResult> QuoteAsync(
            FreightQuoteRequest request,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(new CapturedRequest(request, forceRefresh));
            return Task.FromResult(result);
        }
    }

    private sealed record CapturedRequest(
        FreightQuoteRequest Request,
        bool ForceRefresh);

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
