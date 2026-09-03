using Microsoft.Extensions.Options;
using Sallvat.Application.Catalog;

namespace Sallvat.Infrastructure.Storage;

internal sealed class LocalImageStorage : IImageStorage
{
    private readonly string rootPath;
    private readonly string publicPath;

    public LocalImageStorage(IOptions<ImageStorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        rootPath = Path.GetFullPath(options.Value.RootPath);
        publicPath = options.Value.PublicPath.TrimEnd('/');
        Directory.CreateDirectory(rootPath);
    }

    public async Task WriteAsync(
        string key,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var targetPath = ResolvePath(key);
        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException(
                "Storage target has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81_920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await content.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, targetPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task<Stream?> OpenReadAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(key);
        Stream? stream = File.Exists(path)
            ? new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81_920,
                FileOptions.Asynchronous | FileOptions.SequentialScan)
            : null;

        return Task.FromResult(stream);
    }

    public Task<bool> ExistsAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(ResolvePath(key)));
    }

    public Task DeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public string GetPublicUrl(string key) =>
        $"{publicPath}/{StorageKey.Normalize(key)}";

    private string ResolvePath(string key)
    {
        var normalized = StorageKey.Normalize(key);
        var path = Path.GetFullPath(
            Path.Combine(
                rootPath,
                normalized.Replace('/', Path.DirectorySeparatorChar)));
        var relativePath = Path.GetRelativePath(rootPath, path);
        if (Path.IsPathFullyQualified(relativePath)
            || relativePath.Equals("..", StringComparison.Ordinal)
            || relativePath.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
        {
            throw new ArgumentException("Storage key is invalid.", nameof(key));
        }

        return path;
    }
}
