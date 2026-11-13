using System.Globalization;
using System.Text.Json;
using Sallvat.Application.Catalog;

namespace Sallvat.Web.Seo;

internal static class ProductStructuredData
{
    internal const string BrandName = "Sallvat & Co.";
    internal const string Instagram = "https://www.instagram.com/sallvatco";
    internal const string WhatsApp = "https://wa.me/5511963143754";
    internal const string ContactEmail = "sallvatco@gmail.com";
    internal const string ContactPhone = "+55 11 96314-3754";

    internal static string BuildHome(StorefrontSeo seo) => Serialize(
        new Dictionary<string, object?>
        {
            ["@type"] = seo.IndexingEnabled ? "OnlineStore" : "Organization",
            ["@id"] = seo.Absolute("/#organization"),
            ["name"] = BrandName,
            ["url"] = seo.Absolute("/"),
            ["sameAs"] = new[] { Instagram },
            ["email"] = ContactEmail,
            ["telephone"] = ContactPhone,
        },
        new Dictionary<string, object?>
        {
            ["@type"] = "WebSite",
            ["@id"] = seo.Absolute("/#website"),
            ["name"] = BrandName,
            ["url"] = seo.Absolute("/"),
            ["inLanguage"] = "pt-BR",
            ["publisher"] = new Dictionary<string, string> { ["@id"] = seo.Absolute("/#organization") },
        });

    internal static string Build(CatalogProductDetails product, StorefrontSeo seo)
    {
        var canonicalUrl = seo.Absolute(StorefrontSeo.ProductPath(product.Slug));
        var breadcrumbs = new Dictionary<string, object?>
        {
            ["@type"] = "BreadcrumbList",
            ["@id"] = canonicalUrl + "#breadcrumbs",
            ["itemListElement"] = new[]
            {
                Breadcrumb(1, "Início", seo.Absolute("/")),
                Breadcrumb(2, "Perfumes", seo.Absolute("/perfumes")),
                Breadcrumb(3, product.Name, canonicalUrl),
            },
        };
        var images = product.Images.Select(image => seo.Absolute(image.LargeUrl)).ToArray();
        var groupId = $"sallvat-p-{product.Id.ToString(CultureInfo.InvariantCulture)}";
        var groupEntityId = seo.Absolute("/#" + groupId);
        var group = new Dictionary<string, object?>
        {
            ["@type"] = "ProductGroup",
            ["@id"] = groupEntityId,
            ["url"] = canonicalUrl,
            ["name"] = product.Name,
            ["description"] = product.ShortDescription,
            ["image"] = images,
            ["brand"] = Brand(),
            ["productGroupID"] = groupId,
            ["variesBy"] = "https://schema.org/size",
        };

        if (product.Variants.Count == 1)
        {
            return Serialize(breadcrumbs, Variant(product.Variants[0], grouped: false));
        }

        // Duplicate volumes are still renderable, but cannot identify distinct variants by size.
        if (product.Variants.Select(variant => variant.VolumeMl).Distinct().Count() != product.Variants.Count)
        {
            return Serialize(breadcrumbs);
        }

        group["hasVariant"] = product.Variants.Select(variant => Variant(variant, grouped: true)).ToArray();
        return Serialize(breadcrumbs, group);

        Dictionary<string, object?> Variant(CatalogVariant variant, bool grouped)
        {
            var size = VolumeLabel(variant);
            var variantUrl = seo.Absolute(StorefrontSeo.ProductPath(product.Slug, variant.Id));
            var item = new Dictionary<string, object?>
            {
                ["@type"] = "Product",
                ["@id"] = seo.Absolute($"/#sallvat-v-{variant.Id.ToString(CultureInfo.InvariantCulture)}"),
                ["url"] = grouped ? variantUrl : canonicalUrl,
                ["name"] = grouped ? $"{product.Name} — {size}" : product.Name,
                ["description"] = product.ShortDescription,
                ["image"] = images,
                ["brand"] = Brand(),
                ["sku"] = variant.Sku,
                ["size"] = size,
            };
            if (grouped)
            {
                item["isVariantOf"] = new Dictionary<string, string> { ["@id"] = groupEntityId };
            }

            if (seo.IndexingEnabled)
            {
                var offer = new Dictionary<string, object?>
                {
                    ["@type"] = "Offer",
                    ["url"] = variantUrl,
                    ["priceCurrency"] = variant.Currency,
                    ["price"] = variant.Price,
                    ["availability"] = variant.Available > 0
                        ? "https://schema.org/InStock"
                        : "https://schema.org/OutOfStock",
                    ["seller"] = new Dictionary<string, string> { ["@id"] = seo.Absolute("/#organization") },
                };
                item["offers"] = offer;
                if (seo.MerchantItems.TryGetValue(variant.Id, out var identifiers) && identifiers.IsValid())
                {
                    offer["itemCondition"] = "https://schema.org/NewCondition";
                    if (!string.IsNullOrWhiteSpace(identifiers.Gtin))
                    {
                        item["gtin"] = identifiers.Gtin;
                    }
                    if (!string.IsNullOrWhiteSpace(identifiers.Mpn))
                    {
                        item["mpn"] = identifiers.Mpn;
                    }
                }
            }

            return item;
        }
    }

    internal static string VolumeLabel(CatalogVariant variant) =>
        variant.VolumeMl.ToString(CultureInfo.InvariantCulture) + " ml";

    private static Dictionary<string, string> Brand() =>
        new() { ["@type"] = "Brand", ["name"] = BrandName };

    private static Dictionary<string, object> Breadcrumb(int position, string name, string url) =>
        new Dictionary<string, object>
        {
            ["@type"] = "ListItem",
            ["position"] = position,
            ["name"] = name,
            ["item"] = url,
        };

    private static string Serialize(params object[] entities) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["@context"] = "https://schema.org",
            ["@graph"] = entities,
        });
}
