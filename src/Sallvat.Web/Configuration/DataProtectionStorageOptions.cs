namespace Sallvat.Web.Configuration;

public sealed class DataProtectionStorageOptions
{
    public const string SectionName = "DataProtection";

    public string KeysPath { get; set; } = string.Empty;
}
