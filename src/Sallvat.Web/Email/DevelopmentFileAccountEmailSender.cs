using System.Text;
using System.Text.Encodings.Web;
using Sallvat.Application.Accounts;

namespace Sallvat.Web.Email;

internal sealed class DevelopmentFileAccountEmailSender(
    IWebHostEnvironment environment) : IEmailSender
{
    private readonly string pickupDirectory = Path.GetFullPath(
        Path.Combine(
            environment.ContentRootPath,
            "..",
            "..",
            ".local",
            "emails"));

    public Task SendEmailConfirmationAsync(
        string recipientEmail,
        Uri confirmationUrl,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            recipientEmail,
            "Confirme seu e-mail",
            "Confirme seu e-mail para ativar sua conta.",
            "Confirmar e-mail",
            confirmationUrl,
            cancellationToken);

    public Task SendPasswordResetAsync(
        string recipientEmail,
        Uri resetUrl,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            recipientEmail,
            "Redefina sua senha",
            "Use o link abaixo para escolher uma nova senha.",
            "Redefinir senha",
            resetUrl,
            cancellationToken);

    private async Task WriteAsync(
        string recipientEmail,
        string subject,
        string introduction,
        string callToAction,
        Uri actionUrl,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(pickupDirectory);
        var encoder = HtmlEncoder.Default;
        var body = $$"""
            <!doctype html>
            <html lang="pt-BR">
            <head><meta charset="utf-8"><title>{{encoder.Encode(subject)}}</title></head>
            <body>
              <p>Para: {{encoder.Encode(recipientEmail)}}</p>
              <h1>{{encoder.Encode(subject)}}</h1>
              <p>{{encoder.Encode(introduction)}}</p>
              <p><a href="{{encoder.Encode(actionUrl.AbsoluteUri)}}">{{encoder.Encode(callToAction)}}</a></p>
              <p>Se você não solicitou esta mensagem, ignore-a.</p>
            </body>
            </html>
            """;
        var fileName = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.html";
        var filePath = Path.Combine(pickupDirectory, fileName);

        await File.WriteAllTextAsync(
            filePath,
            body,
            Encoding.UTF8,
            cancellationToken);
    }
}
