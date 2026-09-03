using System.Globalization;
using System.Text.Json;
using Sallvat.Application.Catalog;

namespace Sallvat.Web.Seo;

internal static class ProductStructuredData
{
    public static string Build(
        CatalogProductDetails product,
        Uri canonicalUrl,
        Uri publicOrigin)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(canonicalUrl);
        ArgumentNullException.ThrowIfNull(publicOrigin);

        var images = product.Images
            .Select(image => new Uri(publicOrigin, image.LargeUrl).AbsoluteUri)
            .ToArray();
        var offers = product.Variants
            .Select(variant => new Dictionary<string, object?>
            {
                ["@type"] = "Offer",
                ["sku"] = variant.Sku,
                ["url"] = $"{canonicalUrl.AbsoluteUri}?variante={variant.Id.ToString(CultureInfo.InvariantCulture)}",
                ["priceCurrency"] = variant.Currency,
                ["price"] = variant.Price,
                ["availability"] = variant.Available > 0
                    ? "https://schema.org/InStock"
                    : "https://schema.org/OutOfStock",
                ["itemCondition"] = "https://schema.org/NewCondition",
            })
            .ToArray();
        var document = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Product",
            ["name"] = product.Name,
            ["description"] = product.ShortDescription,
            ["url"] = canonicalUrl.AbsoluteUri,
            ["image"] = images,
            ["brand"] = new Dictionary<string, object?>
            {
                ["@type"] = "Brand",
                ["name"] = "Sallvat & Co.",
            },
            ["offers"] = offers,
        };

        return JsonSerializer.Serialize(document);
    }
}
