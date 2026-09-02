namespace Sallvat.Application.Accounts;

public interface IAccountService
{
    Task<AccountRegistrationResult> RegisterAsync(
        RegisterAccountCommand command,
        CancellationToken cancellationToken = default);

    Task<AccountEmailChallenge?> CreateEmailConfirmationChallengeAsync(
        string email,
        CancellationToken cancellationToken = default);

    Task<bool> ConfirmEmailAsync(
        Guid userId,
        string token,
        CancellationToken cancellationToken = default);

    Task<AccountSignInStatus> SignInAsync(
        string email,
        string password,
        bool rememberMe);

    Task SignOutAsync();

    Task<AccountEmailChallenge?> CreatePasswordResetChallengeAsync(
        string email,
        CancellationToken cancellationToken = default);

    Task<AccountOperationResult> ResetPasswordAsync(
        Guid userId,
        string token,
        string password,
        CancellationToken cancellationToken = default);

    Task<AccountProfile?> GetProfileAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<AccountOperationResult> UpdateProfileAsync(
        Guid userId,
        string name,
        string? phone,
        CancellationToken cancellationToken = default);

    Task<AccountOperationResult> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccountAddress>> ListAddressesAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<AccountOperationResult> CreateAddressAsync(
        Guid userId,
        AddressInput input,
        CancellationToken cancellationToken = default);

    Task<AccountAddress?> GetAddressAsync(
        Guid userId,
        long addressId,
        CancellationToken cancellationToken = default);

    Task<AccountOperationResult> UpdateAddressAsync(
        Guid userId,
        long addressId,
        AddressInput input,
        CancellationToken cancellationToken = default);

    Task<bool> DeactivateAddressAsync(
        Guid userId,
        long addressId,
        CancellationToken cancellationToken = default);
}
