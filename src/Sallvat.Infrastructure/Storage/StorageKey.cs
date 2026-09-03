namespace Sallvat.Infrastructure.Storage;

internal static class StorageKey
{
    public static string Normalize(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var normalized = key.Trim();
        if (normalized.Length > 700
            || normalized[0] is '/' or '\\'
            || normalized.Contains('\\'))
        {
            throw new ArgumentException("Storage key is invalid.", nameof(key));
        }

        var segments = normalized.Split('/');
        if (segments.Any(segment =>
                segment.Length == 0
                || segment is "." or ".."
                || segment.Any(character =>
                    !char.IsAsciiLetterOrDigit(character)
                    && character is not '-' and not '_' and not '.')))
        {
            throw new ArgumentException("Storage key is invalid.", nameof(key));
        }

        return string.Join('/', segments);
    }
}
