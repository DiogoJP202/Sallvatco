namespace Sallvat.Infrastructure.Storage;

public sealed class ImageStorageOptions
{
    public const string SectionName = "ImageStorage";

    public string RootPath { get; set; } = string.Empty;

    public string PublicPath { get; set; } = "/media";

    public long MaximumUploadBytes { get; set; } = 10 * 1024 * 1024;

    public long MaximumPixelCount { get; set; } = 25_000_000;

    public int MaximumDimension { get; set; } = 10_000;

    public int MaximumImagesPerProduct { get; set; } = 10;
}
