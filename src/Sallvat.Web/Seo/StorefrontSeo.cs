using System.Globalization;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Sallvat.Web.Configuration;

namespace Sallvat.Web.Seo;

public sealed class StorefrontSeo
{
    private static readonly string[] InstitutionalPaths = ["/", "/sobre", "/contato", "/entrega"];
    private readonly IWebHostEnvironment environment;
    private readonly SeoOptions options;

    internal StorefrontSeo(
        IWebHostEnvironment environment,
        IOptions<SeoOptions> options,
        IOptions<AccountLinkOptions> accountLinks)
    {
        this.environment = environment;
        this.options = options.Value;
        Origin = new Uri(accountLinks.Value.PublicOrigin);
    }

    public Uri Origin { get; }

    public bool IndexingEnabled => environment.IsProduction() && options.IndexingEnabled;

    internal bool MerchantFeedEnabled => IndexingEnabled && options.MerchantFeedEnabled;

    internal IReadOnlyDictionary<long, MerchantItemOptions> MerchantItems => options.MerchantItems;

    internal bool IsPublicHost => environment.IsProduction();

    internal static IReadOnlyList<string> PublicInstitutionalPaths => InstitutionalPaths;

    internal static bool IsValidProductionOrigin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.IdnHost.TrimEnd('.');
        return uri.Scheme == Uri.UriSchemeHttps
            && uri.HostNameType == UriHostNameType.Dns
            && uri.IsDefaultPort
            && host.Contains('.', StringComparison.Ordinal)
            && !uri.IsLoopback
            && !host.EndsWith(".localhost", StringComparison.Ordinal)
            && !host.EndsWith(".local", StringComparison.Ordinal)
            && !host.EndsWith(".test", StringComparison.Ordinal)
            && !host.EndsWith(".invalid", StringComparison.Ordinal)
            && !host.EndsWith(".example", StringComparison.Ordinal)
            && !host.EndsWith(".github.io", StringComparison.Ordinal)
            && string.IsNullOrEmpty(uri.UserInfo);
    }

    public string Absolute(string path) => new Uri(Origin, path).AbsoluteUri;

    internal static string CatalogPath(int page = 1, string? family = null)
    {
        var path = "/perfumes";
        if (family is not null)
        {
            path = QueryHelpers.AddQueryString(path, "familia", family);
        }

        return page > 1
            ? QueryHelpers.AddQueryString(path, "pagina", page.ToString(CultureInfo.InvariantCulture))
            : path;
    }

    internal static string ProductPath(string slug, long? variantId = null)
    {
        var path = "/perfumes/" + Uri.EscapeDataString(slug);
        return variantId is long id
            ? QueryHelpers.AddQueryString(path, "variante", id.ToString(CultureInfo.InvariantCulture))
            : path;
    }

    internal void SetPage(ViewDataDictionary view, string path, bool eligible, string type = "website")
    {
        view["CanonicalUrl"] = Absolute(path);
        view["OpenGraphType"] = type;
        view["OpenGraphImage"] = Absolute("/images/home/hero-coastal.webp");
        view["OpenGraphImageAlt"] = "Frasco de perfume em vidro sobre pedra clara diante do mar";
        view["OpenGraphImageWidth"] = 1536;
        view["OpenGraphImageHeight"] = 1024;
        view["SeoIndexEligible"] = eligible;
    }

    internal void SetInstitutionalPage(ViewDataDictionary view, string path) =>
        SetPage(view, path, InstitutionalPaths.Contains(path, StringComparer.Ordinal));

    internal static string WithCampaign(string path, IQueryCollection query)
    {
        var campaign = query
            .Where(parameter => (parameter.Key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
                    || parameter.Key is "gclid" or "msclkid" or "fbclid")
                && parameter.Key.Length <= 64
                && parameter.Value.Count == 1
                && parameter.Value[0]?.Length <= 512)
            .Take(12)
            .Select(parameter => new KeyValuePair<string, string?>(parameter.Key, parameter.Value[0]));

        return QueryHelpers.AddQueryString(path, campaign);
    }
}
