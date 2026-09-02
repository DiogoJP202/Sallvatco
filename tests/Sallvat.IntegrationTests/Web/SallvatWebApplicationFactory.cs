using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sallvat.IntegrationTests.Web;

public sealed class SallvatWebApplicationFactory :
    WebApplicationFactory<Program>
{
    private readonly bool ownsDataProtectionKeysPath;

    public SallvatWebApplicationFactory(string? dataProtectionKeysPath = null)
    {
        ownsDataProtectionKeysPath = dataProtectionKeysPath is null;
        DataProtectionKeysPath = dataProtectionKeysPath
            ?? Path.Combine(
                Path.GetTempPath(),
                "Sallvat.Tests",
                Guid.NewGuid().ToString("N"));
    }

    public string DataProtectionKeysPath { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
            services
                .AddControllersWithViews()
                .AddApplicationPart(typeof(FailureController).Assembly));
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SallvatDatabase"] =
                        "Host=127.0.0.1;Port=1;Database=sallvat;" +
                        "Username=sallvat;Password=test;Timeout=1",
                    ["Operational:ServiceName"] = "Sallvat.Tests",
                    ["Operational:CorrelationIdMaxLength"] = "64",
                    ["DataProtection:KeysPath"] = DataProtectionKeysPath,
                });
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        if (ownsDataProtectionKeysPath
            && Directory.Exists(DataProtectionKeysPath))
        {
            Directory.Delete(DataProtectionKeysPath, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
