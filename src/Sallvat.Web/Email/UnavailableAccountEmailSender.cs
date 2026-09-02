using Sallvat.Application.Accounts;

namespace Sallvat.Web.Email;

internal sealed class UnavailableAccountEmailSender : IEmailSender
{
    public Task SendEmailConfirmationAsync(
        string recipientEmail,
        Uri confirmationUrl,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new AccountEmailDeliveryUnavailableException());

    public Task SendPasswordResetAsync(
        string recipientEmail,
        Uri resetUrl,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new AccountEmailDeliveryUnavailableException());
}

internal sealed class AccountEmailDeliveryUnavailableException : Exception
{
    public AccountEmailDeliveryUnavailableException() :
        base("Account email delivery is not configured.")
    {
    }
}
