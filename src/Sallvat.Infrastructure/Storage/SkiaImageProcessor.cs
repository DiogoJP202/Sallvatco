using System.Buffers;
using Microsoft.Extensions.Options;
using Sallvat.Application.Catalog;
using SkiaSharp;

namespace Sallvat.Infrastructure.Storage;

internal sealed class SkiaImageProcessor(
    IOptions<ImageStorageOptions> options) : IImageProcessor
{
    private const int OriginalQuality = 88;
    private const int LargeQuality = 84;
    private const int ThumbnailQuality = 80;
    private readonly ImageStorageOptions settings = options.Value;

    public async Task<ProcessedImage> ProcessAsync(
        ProductImageUpload upload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(upload.Content);
        if (upload.Length <= 0 || upload.Length > settings.MaximumUploadBytes)
        {
            throw new ImageProcessingException(
                "A imagem deve ter até 10 MB e não pode estar vazia.");
        }

        var bytes = await ReadWithLimitAsync(
            upload.Content,
            settings.MaximumUploadBytes,
            cancellationToken);
        var format = DetectFormat(bytes);
        EnsureExtensionMatches(format, upload.FileName);

        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null)
        {
            throw new ImageProcessingException(
                "O arquivo não contém uma imagem válida.");
        }

        var info = codec.Info;
        var pixelCount = (long)info.Width * info.Height;
        if (info.Width <= 0
            || info.Height <= 0
            || info.Width > settings.MaximumDimension
            || info.Height > settings.MaximumDimension
            || pixelCount > settings.MaximumPixelCount)
        {
            throw new ImageProcessingException(
                "A imagem excede o limite de 25 megapixels ou 10.000 pixels por dimensão.");
        }

        if (codec.FrameCount > 1)
        {
            throw new ImageProcessingException(
                "Imagens animadas não são permitidas.");
        }

        using var decoded = SKBitmap.Decode(codec);
        if (decoded is null)
        {
            throw new ImageProcessingException(
                "Não foi possível decodificar completamente a imagem.");
        }

        using var oriented = ApplyOrientation(decoded, codec.EncodedOrigin);

        return new ProcessedImage(
            oriented.Width,
            oriented.Height,
            EncodeWebp(oriented, oriented.Width, oriented.Height, OriginalQuality),
            EncodeWebp(oriented, 1600, 2000, LargeQuality),
            EncodeWebp(oriented, 480, 600, ThumbnailQuality));
    }

    private static async Task<byte[]> ReadWithLimitAsync(
        Stream content,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(81_920);
        try
        {
            int read;
            while ((read = await content.ReadAsync(
                       buffer.AsMemory(0, buffer.Length),
                       cancellationToken)) > 0)
            {
                if (output.Length + read > maximumBytes)
                {
                    throw new ImageProcessingException(
                        "A imagem excede o limite de 10 MB.");
                }

                await output.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken);
            }

            return output.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static InputImageFormat DetectFormat(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8
            && bytes[0] == 0x89
            && bytes[1] == 0x50
            && bytes[2] == 0x4E
            && bytes[3] == 0x47
            && bytes[4] == 0x0D
            && bytes[5] == 0x0A
            && bytes[6] == 0x1A
            && bytes[7] == 0x0A)
        {
            return InputImageFormat.Png;
        }

        if (bytes.Length >= 3
            && bytes[0] == 0xFF
            && bytes[1] == 0xD8
            && bytes[2] == 0xFF)
        {
            return InputImageFormat.Jpeg;
        }

        if (bytes.Length >= 12
            && bytes[..4].SequenceEqual("RIFF"u8)
            && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return InputImageFormat.Webp;
        }

        throw new ImageProcessingException(
            "Formato não permitido. Envie JPEG, PNG ou WebP.");
    }

    private static void EnsureExtensionMatches(
        InputImageFormat format,
        string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var matches = format switch
        {
            InputImageFormat.Jpeg => extension is ".jpg" or ".jpeg",
            InputImageFormat.Png => extension == ".png",
            InputImageFormat.Webp => extension == ".webp",
            _ => false,
        };
        if (!matches)
        {
            throw new ImageProcessingException(
                "A extensão do arquivo não corresponde ao conteúdo da imagem.");
        }
    }

    private static SKBitmap ApplyOrientation(
        SKBitmap source,
        SKEncodedOrigin origin)
    {
        var swapsDimensions = origin is
            SKEncodedOrigin.LeftTop or
            SKEncodedOrigin.RightTop or
            SKEncodedOrigin.RightBottom or
            SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(
            swapsDimensions ? source.Height : source.Width,
            swapsDimensions ? source.Width : source.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);

        switch (origin)
        {
            case SKEncodedOrigin.TopRight:
                canvas.Scale(-1, 1, source.Width / 2f, source.Height / 2f);
                break;
            case SKEncodedOrigin.BottomRight:
                canvas.RotateDegrees(180, source.Width / 2f, source.Height / 2f);
                break;
            case SKEncodedOrigin.BottomLeft:
                canvas.Scale(1, -1, source.Width / 2f, source.Height / 2f);
                break;
            case SKEncodedOrigin.LeftTop:
                canvas.Translate(result.Width, 0);
                canvas.RotateDegrees(90);
                canvas.Scale(-1, 1, source.Width / 2f, source.Height / 2f);
                break;
            case SKEncodedOrigin.RightTop:
                canvas.Translate(result.Width, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom:
                canvas.Translate(0, result.Height);
                canvas.RotateDegrees(-90);
                canvas.Scale(-1, 1, source.Width / 2f, source.Height / 2f);
                break;
            case SKEncodedOrigin.LeftBottom:
                canvas.Translate(0, result.Height);
                canvas.RotateDegrees(-90);
                break;
        }

        canvas.DrawBitmap(
            source,
            0,
            0,
            new SKSamplingOptions(),
            null);
        canvas.Flush();

        return result;
    }

    private static byte[] EncodeWebp(
        SKBitmap source,
        int maximumWidth,
        int maximumHeight,
        int quality)
    {
        var scale = Math.Min(
            1d,
            Math.Min(
                (double)maximumWidth / source.Width,
                (double)maximumHeight / source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        SKBitmap? resized = null;
        try
        {
            if (width != source.Width || height != source.Height)
            {
                resized = source.Resize(
                    new SKImageInfo(
                        width,
                        height,
                        SKColorType.Rgba8888,
                        SKAlphaType.Premul),
                    new SKSamplingOptions(SKCubicResampler.Mitchell));
                if (resized is null)
                {
                    throw new ImageProcessingException(
                        "Não foi possível redimensionar a imagem.");
                }
            }

            using var image = SKImage.FromBitmap(resized ?? source);
            using var encoded = image.Encode(SKEncodedImageFormat.Webp, quality);
            if (encoded is null)
            {
                throw new ImageProcessingException(
                    "Não foi possível gerar a versão WebP.");
            }

            return encoded.ToArray();
        }
        finally
        {
            resized?.Dispose();
        }
    }

    private enum InputImageFormat
    {
        Jpeg,
        Png,
        Webp,
    }
}
