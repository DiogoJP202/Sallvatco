namespace Sallvat.Application.Carts;

public interface ICartService
{
    Task<CartSummary> GetAsync(
        CartOwner owner,
        CancellationToken cancellationToken = default);

    Task<CartMutationResult> AddItemAsync(
        CartOwner owner,
        long variantId,
        int quantity,
        CancellationToken cancellationToken = default);

    Task<CartMutationResult> UpdateItemAsync(
        CartOwner owner,
        long itemId,
        int quantity,
        CancellationToken cancellationToken = default);

    Task<CartMutationResult> RemoveItemAsync(
        CartOwner owner,
        long itemId,
        CancellationToken cancellationToken = default);

    Task<CartMutationResult> ClearAsync(
        CartOwner owner,
        CancellationToken cancellationToken = default);

    Task MergeGuestCartAsync(
        string guestToken,
        Guid applicationUserId,
        CancellationToken cancellationToken = default);

    Task<int> DeleteExpiredAsync(
        int maximumItems,
        CancellationToken cancellationToken = default);
}
