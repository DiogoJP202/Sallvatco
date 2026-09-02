using Sallvat.Domain.Customers;

namespace Sallvat.UnitTests.Customers;

public sealed class CustomerTests
{
    private static readonly DateTimeOffset InitialTimestamp =
        new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CustomerNormalizesContactDataAndAssociatesOneUser()
    {
        var customer = new Customer(
            "  Cliente Teste  ",
            "  cliente@example.com  ",
            "  +55 11 99999-0000  ",
            InitialTimestamp);
        var applicationUserId = Guid.NewGuid();

        customer.AssociateApplicationUser(
            applicationUserId,
            InitialTimestamp.AddMinutes(1));

        Assert.Equal("Cliente Teste", customer.Name);
        Assert.Equal("cliente@example.com", customer.Email);
        Assert.Equal("CLIENTE@EXAMPLE.COM", customer.NormalizedEmail);
        Assert.Equal("+55 11 99999-0000", customer.Phone);
        Assert.Equal(applicationUserId, customer.ApplicationUserId);
    }

    [Fact]
    public void CustomerCannotBeTransferredToAnotherUser()
    {
        var customer = new Customer(
            "Cliente Teste",
            "cliente@example.com",
            null,
            InitialTimestamp);
        customer.AssociateApplicationUser(
            Guid.NewGuid(),
            InitialTimestamp.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() =>
            customer.AssociateApplicationUser(
                Guid.NewGuid(),
                InitialTimestamp.AddMinutes(2)));
    }

    [Fact]
    public void AddressNormalizesBrazilianCodes()
    {
        var address = new Address(
            42,
            " Casa ",
            " Cliente Teste ",
            "01310-100",
            "Avenida Paulista",
            "1000",
            null,
            "Bela Vista",
            "São Paulo",
            "sp",
            InitialTimestamp);

        Assert.Equal("01310100", address.PostalCode);
        Assert.Equal("SP", address.StateCode);
        Assert.Equal("BR", address.CountryCode);
    }

    [Fact]
    public void ProfileAndAddressCanBeUpdatedWithUtcAuditTimestamp()
    {
        var customer = new Customer(
            "Cliente Teste",
            "cliente@example.com",
            null,
            InitialTimestamp);
        var address = new Address(
            42,
            "Casa",
            "Cliente Teste",
            "01310-100",
            "Avenida Paulista",
            "1000",
            null,
            "Bela Vista",
            "São Paulo",
            "SP",
            InitialTimestamp);
        var updatedAt = InitialTimestamp.AddHours(1);

        customer.UpdateProfile(
            "Cliente Atualizado",
            "+55 11 98888-0000",
            updatedAt);
        address.Update(
            "Trabalho",
            "Cliente Atualizado",
            "04538-132",
            "Rua Teste",
            "200",
            "Conjunto 1",
            "Itaim Bibi",
            "São Paulo",
            "sp",
            updatedAt);
        address.Deactivate(updatedAt.AddMinutes(1));

        Assert.Equal("Cliente Atualizado", customer.Name);
        Assert.Equal("+55 11 98888-0000", customer.Phone);
        Assert.Equal("Trabalho", address.Label);
        Assert.Equal("SP", address.StateCode);
        Assert.False(address.IsActive);
    }
}
