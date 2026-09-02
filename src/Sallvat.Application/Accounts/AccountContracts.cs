namespace Sallvat.Application.Accounts;

public sealed record RegisterAccountCommand(
    string Name,
    string Email,
    string? Phone,
    string Password);

public sealed record AccountEmailChallenge(
    Guid UserId,
    string Email,
    string Token);

public sealed record AccountRegistrationResult(
    bool Succeeded,
    IReadOnlyList<string> Errors,
    AccountEmailChallenge? EmailChallenge)
{
    public static AccountRegistrationResult Accepted(
        AccountEmailChallenge? challenge) =>
        new(true, [], challenge);

    public static AccountRegistrationResult Failed(
        IReadOnlyList<string> errors) =>
        new(false, errors, null);
}

public enum AccountSignInStatus
{
    Failed,
    Succeeded,
    LockedOut,
    NotAllowed,
}

public sealed record AccountOperationResult(
    bool Succeeded,
    IReadOnlyList<string> Errors)
{
    public static AccountOperationResult Success() => new(true, []);

    public static AccountOperationResult Failure(
        params string[] errors) =>
        new(false, errors);

    public static AccountOperationResult Failure(
        IReadOnlyList<string> errors) =>
        new(false, errors);
}

public sealed record AccountProfile(
    Guid UserId,
    string Name,
    string Email,
    string? Phone,
    bool EmailConfirmed);

public sealed record AddressInput(
    string Label,
    string RecipientName,
    string PostalCode,
    string Street,
    string Number,
    string? Complement,
    string District,
    string City,
    string StateCode);

public sealed record AccountAddress(
    long Id,
    string Label,
    string RecipientName,
    string PostalCode,
    string Street,
    string Number,
    string? Complement,
    string District,
    string City,
    string StateCode);
