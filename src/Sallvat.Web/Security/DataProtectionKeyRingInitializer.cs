using Microsoft.AspNetCore.DataProtection;

namespace Sallvat.Web.Security;

public sealed class DataProtectionKeyRingInitializer(
    IDataProtectionProvider dataProtectionProvider) : IHostedService
{
    private const string Purpose = "Sallvat.StartupValidation.v1";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var protector = dataProtectionProvider.CreateProtector(Purpose);
        _ = protector.Protect("startup-validation");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
