using Sallvat.Application.Checkout;

namespace Sallvat.UnitTests.Checkout;

public sealed class CheckoutDraftValidatorTests
{
    [Fact]
    public void NormalizesOnlyNecessaryContactAndBrazilianAddressData()
    {
        var (draft, errors) = CheckoutDraftValidator.Validate(new(
            new("  Maria   da Silva ", " MARIA@EXAMPLE.COM ", "(11) 98765-4321"),
            new(
                null,
                " Maria da Silva ",
                "01310-100",
                " Avenida Paulista ",
                " 1000 ",
                " Apto 10 ",
                " Bela Vista ",
                " São Paulo ",
                "sp")));

        Assert.Empty(errors);
        Assert.NotNull(draft);
        Assert.Equal("Maria da Silva", draft.Buyer.Name);
        Assert.Equal("maria@example.com", draft.Buyer.Email);
        Assert.Equal("11987654321", draft.Buyer.Phone);
        Assert.Equal("01310100", draft.Delivery.PostalCode);
        Assert.Equal("SP", draft.Delivery.StateCode);
        Assert.Equal("BR", draft.Delivery.CountryCode);
    }

    [Fact]
    public void RejectsMissingContactAndMalformedDeliveryFields()
    {
        var (draft, errors) = CheckoutDraftValidator.Validate(new(
            new(string.Empty, "invalido", "123"),
            new(
                null,
                string.Empty,
                "123",
                string.Empty,
                string.Empty,
                null,
                string.Empty,
                string.Empty,
                "XX")));

        Assert.Null(draft);
        Assert.Contains(errors, error => error.Field == "BuyerName");
        Assert.Contains(errors, error => error.Field == "Email");
        Assert.Contains(errors, error => error.Field == "Phone");
        Assert.Contains(errors, error => error.Field == "PostalCode");
        Assert.Contains(errors, error => error.Field == "StateCode");
    }
}
