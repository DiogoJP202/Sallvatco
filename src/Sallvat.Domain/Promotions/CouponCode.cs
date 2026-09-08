namespace Sallvat.Domain.Promotions;

public static class CouponCode
{
    public const int MaxLength = 40;

    public static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length is < 3 or > MaxLength
            || normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character != '-'))
        {
            throw new ArgumentException(
                "Coupon code must use 3 to 40 ASCII letters, numbers or hyphens.",
                nameof(value));
        }

        return normalized;
    }
}
