using Microsoft.Extensions.DependencyInjection;
using Sallvat.Application.Accounts;
using Sallvat.Application.Carts;
using Sallvat.Application.Checkout;
using Sallvat.IntegrationTests.Catalog;
using Sallvat.IntegrationTests.Web;

namespace Sallvat.IntegrationTests.Checkout;

public sealed class CheckoutServiceTests
{
    private const string GuestToken =
        "checkout_guest_1234567890123456789012345678";

    [Fact]
    public async Task GuestCheckoutNormalizesDataWithoutCreatingAccount()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        var product = await PublishedCatalogFixture.CreateAsync(
            application,
            "checkout-guest");
        using var scope = application.Services.CreateScope();
        var cartService = scope.ServiceProvider.GetRequiredService<ICartService>();
        var service = scope.ServiceProvider.GetRequiredService<ICheckoutService>();
        var owner = CartOwner.ForGuest(GuestToken);
        Assert.True((await cartService.AddItemAsync(
            owner,
            product.AvailableVariantId,
            1)).Succeeded);

        var result = await service.ValidateAsync(
            owner,
            null,
            ValidInput());

        Assert.True(result.Succeeded);
        Assert.Equal("cliente@example.com", result.Draft!.Buyer.Email);
        Assert.Equal("11999998888", result.Draft.Buyer.Phone);
        Assert.Equal("01310100", result.Draft.Delivery.PostalCode);
        Assert.Equal(299.90m, result.Cart.Total);
    }

    [Fact]
    public async Task MissingFieldsReturnServerSideValidationErrors()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        var product = await PublishedCatalogFixture.CreateAsync(
            application,
            "checkout-invalid");
        using var scope = application.Services.CreateScope();
        var cartService = scope.ServiceProvider.GetRequiredService<ICartService>();
        var service = scope.ServiceProvider.GetRequiredService<ICheckoutService>();
        var owner = CartOwner.ForGuest(GuestToken);
        Assert.True((await cartService.AddItemAsync(
            owner,
            product.AvailableVariantId,
            1)).Succeeded);

        var result = await service.ValidateAsync(
            owner,
            null,
            new CheckoutDraftInput(
                new(string.Empty, string.Empty, string.Empty),
                new(
                    null,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    null,
                    string.Empty,
                    string.Empty,
                    string.Empty)));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Field == "BuyerName");
        Assert.Contains(result.Errors, error => error.Field == "PostalCode");
    }

    [Fact]
    public async Task SavedAddressMustBelongToAuthenticatedCustomer()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        var product = await PublishedCatalogFixture.CreateAsync(
            application,
            "checkout-address-owner");
        using var scope = application.Services.CreateScope();
        var accountService = scope.ServiceProvider
            .GetRequiredService<IAccountService>();
        var cartService = scope.ServiceProvider.GetRequiredService<ICartService>();
        var checkoutService = scope.ServiceProvider
            .GetRequiredService<ICheckoutService>();
        var firstUser = await RegisterAsync(
            accountService,
            "Primeira Pessoa",
            "primeira@example.com");
        var secondUser = await RegisterAsync(
            accountService,
            "Segunda Pessoa",
            "segunda@example.com");
        Assert.True((await accountService.CreateAddressAsync(
            firstUser,
            Address("Casa Primeira", "Rua Própria"))).Succeeded);
        Assert.True((await accountService.CreateAddressAsync(
            secondUser,
            Address("Casa Segunda", "Rua Alheia"))).Succeeded);
        var firstAddress = Assert.Single(
            await accountService.ListAddressesAsync(firstUser));
        var secondAddress = Assert.Single(
            await accountService.ListAddressesAsync(secondUser));
        var owner = CartOwner.ForCustomer(firstUser);
        Assert.True((await cartService.AddItemAsync(
            owner,
            product.AvailableVariantId,
            1)).Succeeded);

        var forbidden = await checkoutService.ValidateAsync(
            owner,
            firstUser,
            ValidInput() with
            {
                Delivery = ValidInput().Delivery with
                {
                    SavedAddressId = secondAddress.Id,
                },
            });
        Assert.False(forbidden.Succeeded);
        Assert.Contains(forbidden.Errors, error =>
            error.Field == "SavedAddressId");

        var own = await checkoutService.ValidateAsync(
            owner,
            firstUser,
            ValidInput() with
            {
                Delivery = ValidInput().Delivery with
                {
                    SavedAddressId = firstAddress.Id,
                    Street = "Valor adulterado",
                },
            });
        Assert.True(own.Succeeded);
        Assert.Equal("Valor adulterado", own.Draft!.Delivery.Street);
        var prefill = await checkoutService.GetPrefillAsync(firstUser);
        Assert.Equal("Primeira Pessoa", prefill.Name);
        Assert.Equal(firstAddress.Id, Assert.Single(prefill.Addresses).Id);
        Assert.Equal(
            "Rua Própria",
            (await accountService.GetAddressAsync(
                firstUser,
                firstAddress.Id))!.Street);
    }

    private static CheckoutDraftInput ValidInput() => new(
        new(" Cliente Teste ", " CLIENTE@EXAMPLE.COM ", "(11) 99999-8888"),
        new(
            null,
            "Cliente Teste",
            "01310-100",
            "Avenida Paulista",
            "1000",
            null,
            "Bela Vista",
            "São Paulo",
            "sp"));

    private static AddressInput Address(string label, string street) => new(
        label,
        "Destinatário",
        "01310100",
        street,
        "100",
        null,
        "Centro",
        "São Paulo",
        "SP");

    private static async Task<Guid> RegisterAsync(
        IAccountService service,
        string name,
        string email)
    {
        var registration = await service.RegisterAsync(new(
            name,
            email,
            "11999998888",
            "Segura#2026Perfume"));
        var challenge = Assert.IsType<AccountEmailChallenge>(
            registration.EmailChallenge);
        Assert.True(await service.ConfirmEmailAsync(
            challenge.UserId,
            challenge.Token));
        return challenge.UserId;
    }
}
