using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Sallvat.Application.Carts;
using Sallvat.Application.Checkout;
using Sallvat.Application.Orders;
using Sallvat.Application.Shipping;
using Sallvat.Infrastructure.Persistence;

namespace Sallvat.Infrastructure.Checkout;

internal sealed class CheckoutService(
    SallvatDbContext dbContext,
    ICartService cartService,
    IFreightService freightService) : ICheckoutService
{
    public async Task<CheckoutPrefill> GetPrefillAsync(
        Guid? applicationUserId,
        CancellationToken cancellationToken = default)
    {
        if (!applicationUserId.HasValue
            || applicationUserId.Value == Guid.Empty)
        {
            return CheckoutPrefill.Empty;
        }

        var customer = await dbContext.Customers
            .AsNoTracking()
            .Where(item =>
                item.ApplicationUserId == applicationUserId.Value)
            .Select(item => new
            {
                item.Id,
                item.Name,
                item.Email,
                item.Phone,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (customer is null)
        {
            return CheckoutPrefill.Empty;
        }

        var addresses = await dbContext.Addresses
            .AsNoTracking()
            .Where(address =>
                address.CustomerId == customer.Id && address.IsActive)
            .OrderBy(address => address.Id)
            .Select(address => new CheckoutSavedAddress(
                address.Id,
                address.Label,
                address.RecipientName,
                address.PostalCode,
                address.Street,
                address.Number,
                address.Complement,
                address.District,
                address.City,
                address.StateCode))
            .ToListAsync(cancellationToken);

        return new CheckoutPrefill(
            customer.Name,
            customer.Email,
            customer.Phone,
            addresses);
    }

    public async Task<CheckoutValidationResult> ValidateAsync(
        CartOwner owner,
        Guid? applicationUserId,
        CheckoutDraftInput input,
        CancellationToken cancellationToken = default)
    {
        var cart = await cartService.GetAsync(owner, cancellationToken);
        if (cart.Items.Count == 0)
        {
            return CheckoutValidationResult.Failure(
                cart,
                new CheckoutValidationError(
                    string.Empty,
                    "Sua sacola está vazia."));
        }

        if (!cart.CanStartCheckout)
        {
            return CheckoutValidationResult.Failure(
                cart,
                new CheckoutValidationError(
                    string.Empty,
                    "Revise a disponibilidade dos itens e do cupom antes de continuar."));
        }

        var delivery = input.Delivery;
        if (delivery.SavedAddressId.HasValue)
        {
            var savedAddress = await FindOwnedAddressAsync(
                applicationUserId,
                delivery.SavedAddressId.Value,
                cancellationToken);
            if (savedAddress is null)
            {
                return CheckoutValidationResult.Failure(
                    cart,
                    new CheckoutValidationError(
                        "SavedAddressId",
                        "O endereço salvo não está disponível para esta conta."));
            }

        }

        var (draft, errors) = CheckoutDraftValidator.Validate(
            input with { Delivery = delivery });
        return draft is null
            ? new CheckoutValidationResult(null, cart, errors)
            : CheckoutValidationResult.Success(draft, cart);
    }

    public async Task<FreightQuoteResult> QuoteFreightAsync(
        CartOwner owner,
        string destinationPostalCode,
        CancellationToken cancellationToken = default)
    {
        var request = await BuildFreightRequestAsync(
            owner,
            destinationPostalCode,
            cancellationToken);
        return request is null
            ? FreightQuoteResult.Failure(
                FreightQuoteStatus.Invalid,
                "A sacola ou o CEP mudou. Revise os dados antes de cotar.")
            : await freightService.QuoteAsync(
                request,
                cancellationToken: cancellationToken);
    }

    public async Task<FreightSelectionResult> RevalidateFreightAsync(
        CartOwner owner,
        string destinationPostalCode,
        string quoteId,
        decimal expectedPrice,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(quoteId) || expectedPrice <= 0)
        {
            return FreightSelectionResult.Failure(
                FreightSelectionStatus.Invalid,
                null,
                "Selecione uma opção de entrega válida.");
        }

        var request = await BuildFreightRequestAsync(
            owner,
            destinationPostalCode,
            cancellationToken);
        if (request is null)
        {
            return FreightSelectionResult.Failure(
                FreightSelectionStatus.Invalid,
                null,
                "A sacola ou o CEP mudou. Revise os dados antes de continuar.");
        }

        var current = await freightService.QuoteAsync(
            request,
            forceRefresh: true,
            cancellationToken);
        if (!current.Succeeded)
        {
            return FreightSelectionResult.Failure(
                FreightSelectionStatus.Unavailable,
                null,
                current.Errors.ToArray());
        }

        var selected = current.Options.SingleOrDefault(option =>
            option.QuoteId.Equals(quoteId, StringComparison.Ordinal));
        if (selected is null)
        {
            return FreightSelectionResult.Failure(
                FreightSelectionStatus.Unavailable,
                null,
                "A opção escolhida não está mais disponível.");
        }

        if (decimal.Round(expectedPrice, 2, MidpointRounding.AwayFromZero)
            != selected.Price)
        {
            return FreightSelectionResult.Failure(
                FreightSelectionStatus.PriceChanged,
                selected,
                "O valor do frete mudou. Confirme a nova cotação antes de continuar.");
        }

        return FreightSelectionResult.Success(
            new CheckoutShippingSnapshot(
                "Melhor Envio",
                selected.Carrier,
                selected.Service,
                selected.QuoteId,
                selected.Price,
                selected.Currency,
                selected.MinimumBusinessDays,
                selected.MaximumBusinessDays,
                selected.QuotedAtUtc,
                selected.ExpiresAtUtc),
            selected);
    }

    private async Task<FreightQuoteRequest?> BuildFreightRequestAsync(
        CartOwner owner,
        string destinationPostalCode,
        CancellationToken cancellationToken)
    {
        var cart = await cartService.GetAsync(owner, cancellationToken);
        if (!cart.CanStartCheckout
            || NormalizePostalCode(
                destinationPostalCode) is not string postalCode)
        {
            return null;
        }

        var quantities = cart.Items.ToDictionary(
            item => item.VariantId,
            item => item.Quantity);
        var variantIds = quantities.Keys.ToArray();
        var items = await dbContext.ProductVariants
            .AsNoTracking()
            .Where(variant => variantIds.Contains(variant.Id))
            .OrderBy(variant => variant.Id)
            .Select(variant => new FreightQuoteItem(
                variant.Id.ToString(CultureInfo.InvariantCulture),
                quantities[variant.Id],
                variant.Price,
                variant.Currency,
                variant.WeightKg,
                variant.HeightCm,
                variant.WidthCm,
                variant.LengthCm))
            .ToListAsync(cancellationToken);
        return items.Count == quantities.Count
            ? new FreightQuoteRequest(postalCode, items)
            : null;
    }

    private static string? NormalizePostalCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 8 ? digits : null;
    }

    private async Task<CheckoutDeliveryInput?> FindOwnedAddressAsync(
        Guid? applicationUserId,
        long addressId,
        CancellationToken cancellationToken)
    {
        if (!applicationUserId.HasValue
            || applicationUserId.Value == Guid.Empty
            || addressId <= 0)
        {
            return null;
        }

        return await (
            from address in dbContext.Addresses.AsNoTracking()
            join customer in dbContext.Customers.AsNoTracking()
                on address.CustomerId equals customer.Id
            where address.Id == addressId
                && address.IsActive
                && customer.ApplicationUserId == applicationUserId.Value
            select new CheckoutDeliveryInput(
                address.Id,
                address.RecipientName,
                address.PostalCode,
                address.Street,
                address.Number,
                address.Complement,
                address.District,
                address.City,
                address.StateCode))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
