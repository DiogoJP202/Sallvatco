using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Net.Http.Headers;
using Sallvat.Application.Catalog;
using Sallvat.Web.Seo;

namespace Sallvat.Web.Controllers;

[AllowAnonymous]
public sealed partial class DiscoveryController(
    StorefrontSeo seo,
    ICatalogService catalog,
    IMemoryCache cache,
    ILogger<DiscoveryController> logger) : Controller
{
    private const int SitemapLimit = 50_000;
    private static readonly XNamespace SitemapNamespace = "http://www.sitemaps.org/schemas/sitemap/0.9";
    private static readonly XNamespace MerchantNamespace = "http://base.google.com/ns/1.0";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    [HttpGet("/robots.txt")]
    [HttpHead("/robots.txt")]
    public ContentResult Robots()
    {
        var text = seo.IsPublicHost
            ? "User-agent: *\nDisallow:\n\nUser-agent: GPTBot\nDisallow: /\n\nUser-agent: ClaudeBot\nDisallow: /\n"
            : "User-agent: *\nDisallow: /\n";

        if (seo.IndexingEnabled)
        {
            text += "\nSitemap: " + seo.Absolute("/sitemap.xml") + "\n";
        }

        return Content(text, "text/plain", Encoding.UTF8);
    }

    [HttpGet("/sitemap.xml")]
    [HttpHead("/sitemap.xml")]
    public Task<IActionResult> Sitemap(CancellationToken cancellationToken) =>
        ReadSitemapAsync(null, cancellationToken);

    [HttpGet("/sitemap-{part:int}.xml")]
    [HttpHead("/sitemap-{part:int}.xml")]
    public Task<IActionResult> SitemapPart(int part, CancellationToken cancellationToken) =>
        ReadSitemapAsync(part, cancellationToken);

    private async Task<IActionResult> ReadSitemapAsync(int? part, CancellationToken cancellationToken)
    {
        if (!seo.IndexingEnabled)
        {
            return NotFound();
        }

        try
        {
            var documents = await cache.GetOrCreateAsync("seo:sitemap:documents", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheLifetime;
                var slugs = await catalog.ListPublicSlugsAsync(cancellationToken);
                var basePaths = StorefrontSeo.PublicInstitutionalPaths;
                if (slugs.Count > 0)
                {
                    basePaths = [.. basePaths, "/perfumes"];
                }

                var paths = basePaths.Concat(slugs.Select(slug => StorefrontSeo.ProductPath(slug)));
                return paths.Chunk(SitemapLimit)
                    .Select(chunk => EncodeXml(new XElement(SitemapNamespace + "urlset",
                        chunk.Select(path => new XElement(SitemapNamespace + "url",
                            new XElement(SitemapNamespace + "loc", seo.Absolute(path)))))))
                    .ToArray();
            }) ?? throw new InvalidOperationException("Sitemap cache entry is missing.");

            var partCount = documents.Length;
            if (part is not null && (partCount == 1 || part < 1 || part > partCount))
            {
                return NotFound();
            }

            var document = part is null && partCount > 1
                ? EncodeXml(new XElement(SitemapNamespace + "sitemapindex",
                    Enumerable.Range(1, partCount).Select(number =>
                        new XElement(SitemapNamespace + "sitemap",
                            new XElement(SitemapNamespace + "loc",
                                seo.Absolute($"/sitemap-{number.ToString(CultureInfo.InvariantCulture)}.xml"))))))
                : documents[(part ?? 1) - 1];

            return Xml(document);
        }
        catch (Exception exception) when (exception is DbException || exception.InnerException is DbException)
        {
            return CatalogUnavailable("sitemap");
        }
    }

    [HttpGet("/feeds/google-merchant.xml")]
    [HttpHead("/feeds/google-merchant.xml")]
    public async Task<IActionResult> MerchantFeed(CancellationToken cancellationToken)
    {
        if (!seo.MerchantFeedEnabled || seo.MerchantItems.Count == 0)
        {
            return NotFound();
        }

        Response.Headers["X-Robots-Tag"] = "noindex";
        try
        {
            var feed = await cache.GetOrCreateAsync("seo:merchant:xml", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheLifetime;
                var started = Stopwatch.GetTimestamp();
                var products = await catalog.ListDiscoveryProductsAsync(seo.MerchantItems.Keys.ToArray(), cancellationToken);
                var items = new List<XElement>();
                var invalidGroups = 0;
                foreach (var product in products)
                {
                    if (product.Variants.Select(variant => variant.VolumeMl).Distinct().Count() != product.Variants.Count
                        || product.Images.All(image => !image.IsCover))
                    {
                        invalidGroups++;
                        continue;
                    }

                    foreach (var variant in product.Variants.Where(variant =>
                        variant.Currency == "BRL" && seo.MerchantItems.ContainsKey(variant.Id)))
                    {
                        var identifiers = seo.MerchantItems[variant.Id];
                        items.Add(MerchantItem(product, variant, identifiers));
                    }
                }

                var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                FeedGenerated(logger, items.Count, seo.MerchantItems.Count - items.Count, invalidGroups, elapsedMs);
                return EncodeXml(new XElement("rss",
                    new XAttribute("version", "2.0"),
                    new XAttribute(XNamespace.Xmlns + "g", MerchantNamespace),
                    new XElement("channel",
                        new XElement("title", ProductStructuredData.BrandName),
                        new XElement("link", seo.Absolute("/")),
                        new XElement("description", "Catálogo de fragrâncias Sallvat & Co."),
                        items)));
            });

            return Xml(feed ?? throw new InvalidOperationException("Merchant cache entry is missing."));
        }
        catch (Exception exception) when (exception is DbException || exception.InnerException is DbException)
        {
            return CatalogUnavailable("merchant");
        }
    }

    private XElement MerchantItem(CatalogDiscoveryProduct product, CatalogVariant variant, MerchantItemOptions identifiers)
    {
        var volume = ProductStructuredData.VolumeLabel(variant);
        var cover = product.Images.First(image => image.IsCover);
        var grouped = product.Variants.Count > 1;
        return new XElement("item",
            Attribute("id", $"sallvat-v-{variant.Id.ToString(CultureInfo.InvariantCulture)}"),
            Attribute("title", $"{product.Name} — {volume}"),
            Attribute("description", product.Description),
            Attribute("link", seo.Absolute(StorefrontSeo.ProductPath(product.Slug, variant.Id))),
            Attribute("canonical_link", seo.Absolute(StorefrontSeo.ProductPath(product.Slug))),
            Attribute("image_link", seo.Absolute(cover.LargeUrl)),
            product.Images.Where(image => !image.IsCover).Select(image => Attribute("additional_image_link", seo.Absolute(image.LargeUrl))),
            Attribute("brand", ProductStructuredData.BrandName),
            Attribute("condition", "new"),
            Attribute("price", variant.Price.ToString("0.00", CultureInfo.InvariantCulture) + " BRL"),
            Attribute("availability", variant.Available > 0 ? "in_stock" : "out_of_stock"),
            grouped ? Attribute("item_group_id", $"sallvat-p-{product.Id.ToString(CultureInfo.InvariantCulture)}") : null,
            grouped ? Attribute("item_group_title", product.Name) : null,
            Attribute("size", volume),
            grouped ? Attribute("variant_option", Attribute("name", "volume"), Attribute("value", volume)) : null,
            string.IsNullOrWhiteSpace(identifiers.Gtin) ? null : Attribute("gtin", identifiers.Gtin),
            string.IsNullOrWhiteSpace(identifiers.Mpn) ? null : Attribute("mpn", identifiers.Mpn),
            identifiers.NoAssignedIdentifiers ? Attribute("identifier_exists", "no") : null);
    }

    private static XElement Attribute(string name, params object[] content) =>
        new(MerchantNamespace + name, content);

    private static byte[] EncodeXml(XElement document) =>
        Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            + document.ToString(SaveOptions.DisableFormatting));

    private IActionResult Xml(byte[] bytes)
    {
        var tag = new EntityTagHeaderValue("\"" + Convert.ToHexString(SHA256.HashData(bytes)) + "\"");
        Response.GetTypedHeaders().ETag = tag;
        Response.Headers.CacheControl = "private, no-cache";
        if (Request.GetTypedHeaders().IfNoneMatch.Any(value =>
            value == EntityTagHeaderValue.Any || tag.Compare(value, useStrongComparison: false)))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        return File(bytes, "application/xml; charset=utf-8");
    }

    private StatusCodeResult CatalogUnavailable(string resource)
    {
        DiscoveryUnavailable(logger, resource);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.RetryAfter = "300";
        return StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    [LoggerMessage(EventId = 4601, Level = LogLevel.Information,
        Message = "Merchant feed generated {Items} items, excluded {Excluded} configured variants across {InvalidGroups} ambiguous groups, in {ElapsedMs} ms.")]
    private static partial void FeedGenerated(ILogger logger, int items, int excluded, int invalidGroups, double elapsedMs);

    [LoggerMessage(EventId = 4602, Level = LogLevel.Warning,
        Message = "Discovery resource {Resource} unavailable because the catalog could not be read.")]
    private static partial void DiscoveryUnavailable(ILogger logger, string resource);
}
