using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Sallvat.IntegrationTests.Web;

public sealed class DataProtectionTests
{
    [Fact]
    public async Task KeyRingPersistsAcrossApplicationRestarts()
    {
        var keysPath = Path.Combine(
            Path.GetTempPath(),
            "Sallvat.Tests.SharedKeys",
            Guid.NewGuid().ToString("N"));

        try
        {
            string protectedValue;

            await using (var firstApplication =
                         new SallvatWebApplicationFactory(keysPath))
            {
                using var client = firstApplication.CreateClient();
                var firstProtector = firstApplication.Services
                    .GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("Sallvat.Tests.Restart.v1");

                protectedValue = firstProtector.Protect("protected-value");
            }

            await using (var secondApplication =
                         new SallvatWebApplicationFactory(keysPath))
            {
                using var client = secondApplication.CreateClient();
                var secondProtector = secondApplication.Services
                    .GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("Sallvat.Tests.Restart.v1");

                Assert.Equal(
                    "protected-value",
                    secondProtector.Unprotect(protectedValue));
            }

            Assert.Single(Directory.EnumerateFiles(keysPath, "key-*.xml"));
        }
        finally
        {
            if (Directory.Exists(keysPath))
            {
                Directory.Delete(keysPath, recursive: true);
            }
        }
    }
}
