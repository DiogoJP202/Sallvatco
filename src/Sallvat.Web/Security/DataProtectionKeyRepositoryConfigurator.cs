using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;
using Sallvat.Web.Configuration;

namespace Sallvat.Web.Security;

public sealed class DataProtectionKeyRepositoryConfigurator(
    IOptions<DataProtectionStorageOptions> storageOptions,
    ILoggerFactory loggerFactory) : IConfigureOptions<KeyManagementOptions>
{
    public void Configure(KeyManagementOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var directory = Directory.CreateDirectory(
            storageOptions.Value.KeysPath);

        options.XmlRepository = new FileSystemXmlRepository(
            directory,
            loggerFactory);
    }
}
