using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Sallvat.Infrastructure.Shipping;

public sealed partial class MelhorEnvioOptions
{
    public const string SectionName = "Shipping:MelhorEnvio";

    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } =
        "https://sandbox.melhorenvio.com.br/";

    public string OriginPostalCode { get; set; } = string.Empty;

    public string AccessToken { get; set; } = string.Empty;

    public string ApplicationName { get; set; } = "Sallvat";

    public string SupportEmail { get; set; } = string.Empty;

    public string? ServiceIds { get; set; }

    public int TimeoutSeconds { get; set; } = 10;

    public int CacheSeconds { get; set; } = 120;

    public int QuoteValiditySeconds { get; set; } = 600;

    internal static bool IsValid(MelhorEnvioOptions options)
    {
        if (options.TimeoutSeconds is < 2 or > 30
            || options.CacheSeconds is < 10 or > 300
            || options.QuoteValiditySeconds < options.CacheSeconds
            || options.QuoteValiditySeconds > 900)
        {
            return false;
        }

        if (!options.Enabled)
        {
            return true;
        }

        return Uri.TryCreate(
                options.BaseUrl,
                UriKind.Absolute,
                out var baseUri)
            && baseUri.Scheme == Uri.UriSchemeHttps
            && NormalizePostalCode(options.OriginPostalCode) is not null
            && !string.IsNullOrWhiteSpace(options.AccessToken)
            && !string.IsNullOrWhiteSpace(options.ApplicationName)
            && options.ApplicationName.Trim().Length <= 50
            && MailAddress.TryCreate(options.SupportEmail, out _)
            && (string.IsNullOrWhiteSpace(options.ServiceIds)
                || ServiceIdsPattern().IsMatch(options.ServiceIds.Trim()));
    }

    internal static string? NormalizePostalCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 8 ? digits : null;
    }

    [GeneratedRegex("^[0-9]+(?:,[0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex ServiceIdsPattern();
}
