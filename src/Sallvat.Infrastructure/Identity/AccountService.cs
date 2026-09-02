using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sallvat.Application.Accounts;
using Sallvat.Application.Authorization;
using Sallvat.Application.Time;
using Sallvat.Domain.Customers;
using Sallvat.Infrastructure.Persistence;

namespace Sallvat.Infrastructure.Identity;

internal sealed class AccountService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    SallvatDbContext dbContext,
    IClock clock) : IAccountService
{
    public async Task<AccountRegistrationResult> RegisterAsync(
        RegisterAccountCommand command,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var email = command.Email.Trim();
        var existingUser = await userManager.FindByEmailAsync(email);
        if (existingUser is not null)
        {
            return AccountRegistrationResult.Accepted(null);
        }

        await using var transaction = await BeginTransactionAsync(
            cancellationToken);
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            UserName = email,
        };
        var createResult = await userManager.CreateAsync(
            user,
            command.Password);

        if (!createResult.Succeeded)
        {
            await RollbackAsync(transaction, cancellationToken);

            if (createResult.Errors.Any(error =>
                    error.Code is "DuplicateEmail" or "DuplicateUserName"))
            {
                return AccountRegistrationResult.Accepted(null);
            }

            return AccountRegistrationResult.Failed(
                TranslateErrors(createResult));
        }

        var roleResult = await userManager.AddToRoleAsync(
            user,
            RoleNames.Customer);
        if (!roleResult.Succeeded)
        {
            await RollbackOrDeleteUserAsync(
                transaction,
                user,
                cancellationToken);

            return AccountRegistrationResult.Failed(
                ["Não foi possível concluir o cadastro. Tente novamente."]);
        }

        var customer = new Customer(
            command.Name,
            email,
            command.Phone,
            clock.UtcNow);
        customer.AssociateApplicationUser(user.Id, clock.UtcNow);
        dbContext.Customers.Add(customer);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            await RollbackOrDeleteUserAsync(
                transaction,
                user,
                cancellationToken);
            throw;
        }

        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);

        return AccountRegistrationResult.Accepted(
            new AccountEmailChallenge(user.Id, email, token));
    }

    public async Task<AccountEmailChallenge?>
        CreateEmailConfirmationChallengeAsync(
            string email,
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByEmailAsync(email.Trim());
        if (user is null || user.EmailConfirmed || user.Email is null)
        {
            return null;
        }

        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);

        return new AccountEmailChallenge(user.Id, user.Email, token);
    }

    public async Task<bool> ConfirmEmailAsync(
        Guid userId,
        string token,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return false;
        }

        var result = await userManager.ConfirmEmailAsync(user, token);

        return result.Succeeded;
    }

    public async Task<AccountSignInStatus> SignInAsync(
        string email,
        string password,
        bool rememberMe)
    {
        var result = await signInManager.PasswordSignInAsync(
            email.Trim(),
            password,
            rememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return AccountSignInStatus.Succeeded;
        }

        if (result.IsLockedOut)
        {
            return AccountSignInStatus.LockedOut;
        }

        return result.IsNotAllowed
            ? AccountSignInStatus.NotAllowed
            : AccountSignInStatus.Failed;
    }

    public Task SignOutAsync() => signInManager.SignOutAsync();

    public async Task<AccountEmailChallenge?>
        CreatePasswordResetChallengeAsync(
            string email,
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByEmailAsync(email.Trim());
        if (user is null
            || !await userManager.IsEmailConfirmedAsync(user)
            || user.Email is null)
        {
            return null;
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);

        return new AccountEmailChallenge(user.Id, user.Email, token);
    }

    public async Task<AccountOperationResult> ResetPasswordAsync(
        Guid userId,
        string token,
        string password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return AccountOperationResult.Failure(
                "O link de recuperação é inválido ou expirou.");
        }

        var result = await userManager.ResetPasswordAsync(
            user,
            token,
            password);

        return result.Succeeded
            ? AccountOperationResult.Success()
            : AccountOperationResult.Failure(TranslateErrors(result));
    }

    public Task<AccountProfile?> GetProfileAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        (from customer in dbContext.Customers.AsNoTracking()
         join user in dbContext.Users.AsNoTracking()
             on customer.ApplicationUserId equals user.Id
         where user.Id == userId
         select new AccountProfile(
             user.Id,
             customer.Name,
             customer.Email,
             customer.Phone,
             user.EmailConfirmed))
        .SingleOrDefaultAsync(cancellationToken);

    public async Task<AccountOperationResult> UpdateProfileAsync(
        Guid userId,
        string name,
        string? phone,
        CancellationToken cancellationToken = default)
    {
        var customer = await dbContext.Customers.SingleOrDefaultAsync(
            item => item.ApplicationUserId == userId,
            cancellationToken);
        if (customer is null)
        {
            return AccountOperationResult.Failure(
                "Não foi possível localizar o perfil.");
        }

        customer.UpdateProfile(name, phone, clock.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);

        return AccountOperationResult.Success();
    }

    public async Task<AccountOperationResult> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return AccountOperationResult.Failure(
                "Não foi possível localizar a conta.");
        }

        var result = await userManager.ChangePasswordAsync(
            user,
            currentPassword,
            newPassword);
        if (!result.Succeeded)
        {
            return AccountOperationResult.Failure(TranslateErrors(result));
        }

        await signInManager.RefreshSignInAsync(user);

        return AccountOperationResult.Success();
    }

    public async Task<IReadOnlyList<AccountAddress>> ListAddressesAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await OwnedAddresses(userId)
            .Where(address => address.IsActive)
            .OrderBy(address => address.Label)
            .Select(address => new AccountAddress(
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

    public async Task<AccountOperationResult> CreateAddressAsync(
        Guid userId,
        AddressInput input,
        CancellationToken cancellationToken = default)
    {
        var customerId = await dbContext.Customers
            .Where(customer => customer.ApplicationUserId == userId)
            .Select(customer => (long?)customer.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (customerId is null)
        {
            return AccountOperationResult.Failure(
                "Não foi possível localizar o perfil.");
        }

        dbContext.Addresses.Add(CreateAddress(customerId.Value, input));
        await dbContext.SaveChangesAsync(cancellationToken);

        return AccountOperationResult.Success();
    }

    public Task<AccountAddress?> GetAddressAsync(
        Guid userId,
        long addressId,
        CancellationToken cancellationToken = default) =>
        OwnedAddresses(userId)
            .Where(address => address.Id == addressId && address.IsActive)
            .Select(address => new AccountAddress(
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
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<AccountOperationResult> UpdateAddressAsync(
        Guid userId,
        long addressId,
        AddressInput input,
        CancellationToken cancellationToken = default)
    {
        var address = await OwnedAddresses(userId)
            .SingleOrDefaultAsync(
                item => item.Id == addressId && item.IsActive,
                cancellationToken);
        if (address is null)
        {
            return AccountOperationResult.Failure(
                "Endereço não encontrado.");
        }

        address.Update(
            input.Label,
            input.RecipientName,
            input.PostalCode,
            input.Street,
            input.Number,
            input.Complement,
            input.District,
            input.City,
            input.StateCode,
            clock.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);

        return AccountOperationResult.Success();
    }

    public async Task<bool> DeactivateAddressAsync(
        Guid userId,
        long addressId,
        CancellationToken cancellationToken = default)
    {
        var address = await OwnedAddresses(userId)
            .SingleOrDefaultAsync(
                item => item.Id == addressId && item.IsActive,
                cancellationToken);
        if (address is null)
        {
            return false;
        }

        address.Deactivate(clock.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    private IQueryable<Address> OwnedAddresses(Guid userId) =>
        from address in dbContext.Addresses
        join customer in dbContext.Customers
            on address.CustomerId equals customer.Id
        where customer.ApplicationUserId == userId
        select address;

    private Address CreateAddress(long customerId, AddressInput input) =>
        new(
            customerId,
            input.Label,
            input.RecipientName,
            input.PostalCode,
            input.Street,
            input.Number,
            input.Complement,
            input.District,
            input.City,
            input.StateCode,
            clock.UtcNow);

    private async Task<IDbContextTransaction?> BeginTransactionAsync(
        CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

    private static async Task RollbackAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }

    private async Task RollbackOrDeleteUserAsync(
        IDbContextTransaction? transaction,
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        await userManager.DeleteAsync(user);
    }

    private static string[] TranslateErrors(
        IdentityResult result) =>
        result.Errors
            .Select(error => error.Code switch
            {
                "PasswordTooShort" =>
                    "A senha deve ter pelo menos 12 caracteres.",
                "PasswordRequiresNonAlphanumeric" =>
                    "A senha deve conter um caractere especial.",
                "PasswordRequiresDigit" =>
                    "A senha deve conter um número.",
                "PasswordRequiresLower" =>
                    "A senha deve conter uma letra minúscula.",
                "PasswordRequiresUpper" =>
                    "A senha deve conter uma letra maiúscula.",
                "PasswordRequiresUniqueChars" =>
                    "A senha deve conter ao menos quatro caracteres diferentes.",
                "PasswordMismatch" => "A senha atual está incorreta.",
                "InvalidToken" =>
                    "O link de recuperação é inválido ou expirou.",
                _ => "Não foi possível concluir a operação. Tente novamente.",
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}
