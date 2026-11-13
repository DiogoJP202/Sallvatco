namespace Sallvat.Web.Seo;

internal sealed class SeoOptions
{
    public bool IndexingEnabled { get; set; }

    public bool MerchantFeedEnabled { get; set; }

    public Dictionary<long, MerchantItemOptions> MerchantItems { get; set; } = [];
}

internal sealed class MerchantItemOptions
{
    public string? Gtin { get; set; }

    public string? Mpn { get; set; }

    public bool NoAssignedIdentifiers { get; set; }

    internal bool IsValid()
    {
        if (NoAssignedIdentifiers)
        {
            return string.IsNullOrWhiteSpace(Gtin) && string.IsNullOrWhiteSpace(Mpn);
        }

        return (string.IsNullOrWhiteSpace(Gtin) || IsValidGtin(Gtin))
            && (string.IsNullOrWhiteSpace(Mpn) || Mpn.Length <= 70)
            && (!string.IsNullOrWhiteSpace(Gtin) || !string.IsNullOrWhiteSpace(Mpn));
    }

    private static bool IsValidGtin(string gtin)
    {
        if (gtin.Length is not (8 or 12 or 13 or 14) || !gtin.All(char.IsAsciiDigit))
        {
            return false;
        }

        var sum = 0;
        for (var position = gtin.Length - 2; position >= 0; position--)
        {
            sum += (gtin[position] - '0') * ((gtin.Length - position) % 2 == 0 ? 3 : 1);
        }

        return (10 - sum % 10) % 10 == gtin[^1] - '0';
    }
}
