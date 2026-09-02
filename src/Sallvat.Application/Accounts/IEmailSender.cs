namespace Sallvat.Application.Accounts;

public interface IEmailSender
{
    Task SendEmailConfirmationAsync(
        string recipientEmail,
        Uri confirmationUrl,
        CancellationToken cancellationToken = default);

    Task SendPasswordResetAsync(
        string recipientEmail,
        Uri resetUrl,
        CancellationToken cancellationToken = default);
}
