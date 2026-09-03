using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sallvat.IntegrationTests.Web;

public class SallvatWebApplicationFactory :
    WebApplicationFactory<Program>
{
    private readonly bool ownsDataProtectionKeysPath;
    private readonly bool ownsImageStoragePath;
    private readonly long maximumPixelCount;

    public SallvatWebApplicationFactory(
        string? dataProtectionKeysPath = null,
        string? imageStoragePath = null,
        long? maximumPixelCount = null)
    {
        ownsDataProtectionKeysPath = dataProtectionKeysPath is null;
        ownsImageStoragePath = imageStoragePath is null;
        this.maximumPixelCount = maximumPixelCount ?? 25_000_000;
        DataProtectionKeysPath = dataProtectionKeysPath
            ?? Path.Combine(
                Path.GetTempPath(),
                "Sallvat.Tests",
                Guid.NewGuid().ToString("N"));
        ImageStoragePath = imageStoragePath
            ?? Path.Combine(
                Path.GetTempPath(),
                "Sallvat.Tests.Images",
                Guid.NewGuid().ToString("N"));
    }

    public string DataProtectionKeysPath { get; }

    public string ImageStoragePath { get; }

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
                    ["ImageStorage:RootPath"] = ImageStoragePath,
                    ["ImageStorage:MaximumPixelCount"] =
                        maximumPixelCount.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                    ["AccountLinks:PublicOrigin"] =
                        "https://tests.sallvat.invalid",
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

        if (ownsImageStoragePath && Directory.Exists(ImageStoragePath))
        {
            Directory.Delete(ImageStoragePath, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
