using Sallvat.Application.Catalog;

namespace Sallvat.Web.Models.Catalog;

public static class CatalogImagePresentation
{
    public static string SourceSet(CatalogImage image)
    {
        var thumbnailWidth = ScaledWidth(image, 480, 600);
        var largeWidth = ScaledWidth(image, 1600, 2000);

        return thumbnailWidth == largeWidth
            ? $"{image.LargeUrl} {largeWidth}w"
            : $"{image.ThumbnailUrl} {thumbnailWidth}w, {image.LargeUrl} {largeWidth}w";
    }

    private static int ScaledWidth(CatalogImage image, int maximumWidth, int maximumHeight)
    {
        var scale = Math.Min(1d, Math.Min(
            (double)maximumWidth / image.Width,
            (double)maximumHeight / image.Height));

        return Math.Max(1, (int)Math.Round(image.Width * scale));
    }
}
