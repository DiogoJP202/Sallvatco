using Sallvat.Application.Catalog;

namespace Sallvat.Infrastructure.Storage;

internal interface IImageProcessor
{
    Task<ProcessedImage> ProcessAsync(
        ProductImageUpload upload,
        CancellationToken cancellationToken = default);
}

internal sealed record ProcessedImage(
    int Width,
    int Height,
    byte[] Original,
    byte[] Large,
    byte[] Thumbnail);
