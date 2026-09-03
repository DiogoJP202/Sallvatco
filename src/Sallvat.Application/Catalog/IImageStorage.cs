namespace Sallvat.Application.Catalog;

public interface IImageStorage
{
    Task WriteAsync(
        string key,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string key,
        CancellationToken cancellationToken = default);

    string GetPublicUrl(string key);
}
