namespace Sallvat.Web.Configuration;

public sealed class AccountLinkOptions
{
    public const string SectionName = "AccountLinks";

    public string PublicOrigin { get; set; } = string.Empty;
}
