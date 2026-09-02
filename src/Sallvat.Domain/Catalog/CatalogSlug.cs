using System.Globalization;
using System.Text;

namespace Sallvat.Domain.Catalog;

public static class CatalogSlug
{
    public const int MaxLength = 180;

    public static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousWasSeparator = false;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) ==
                UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var normalized = char.ToLowerInvariant(character);
            if (normalized is >= 'a' and <= 'z'
                or >= '0' and <= '9')
            {
                builder.Append(normalized);
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator && builder.Length > 0)
            {
                builder.Append('-');
                previousWasSeparator = true;
            }
        }

        var slug = builder.ToString().TrimEnd('-');
        if (slug.Length == 0)
        {
            throw new ArgumentException(
                "Slug must contain at least one ASCII letter or digit.",
                nameof(value));
        }

        if (slug.Length > MaxLength)
        {
            slug = slug[..MaxLength].TrimEnd('-');
        }

        return slug;
    }
}
