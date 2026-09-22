using Sallvat.Domain.Payments;

namespace Sallvat.Infrastructure.Payments;

public sealed class MercadoPagoOptions
{
    public const string SectionName = "Payments:MercadoPago";

    public bool Enabled { get; set; }

    public bool OrdersEnabled { get; set; }

    public PaymentEnvironment Environment { get; set; } = PaymentEnvironment.Sandbox;

    public string AccessToken { get; set; } = string.Empty;

    public string PublicOrigin { get; set; } = string.Empty;

    public long TestSellerId { get; set; }

    public bool TestSellerConfirmed { get; set; }

    public int TimeoutSeconds { get; set; } = 10;

    internal static bool IsValid(MercadoPagoOptions options)
    {
        if (options.TimeoutSeconds is < 2 or > 30
            || options.Environment != PaymentEnvironment.Sandbox
            || (options.Enabled && options.OrdersEnabled))
        {
            return false;
        }

        if (!options.Enabled && !options.OrdersEnabled)
        {
            return true;
        }

        return options.TestSellerConfirmed && options.TestSellerId > 0
            && !string.IsNullOrWhiteSpace(options.AccessToken)
            && options.AccessToken.Length <= 512
            && options.AccessToken.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            && Uri.TryCreate(options.PublicOrigin, UriKind.Absolute, out var origin)
            && origin.Scheme == Uri.UriSchemeHttps
            && origin.HostNameType == UriHostNameType.Dns
            && !origin.IsLoopback && origin.IsDefaultPort
            && origin.AbsolutePath == "/"
            && string.IsNullOrEmpty(origin.UserInfo)
            && string.IsNullOrEmpty(origin.Query)
            && string.IsNullOrEmpty(origin.Fragment);
    }
}
