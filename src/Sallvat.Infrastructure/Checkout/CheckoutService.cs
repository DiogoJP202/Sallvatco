using Microsoft.EntityFrameworkCore;
using Sallvat.Application.Carts;
using Sallvat.Application.Checkout;
using Sallvat.Infrastructure.Persistence;

namespace Sallvat.Infrastructure.Checkout;

internal sealed class CheckoutService(
    SallvatDbContext dbContext,
    ICartService cartService) : ICheckoutService
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
