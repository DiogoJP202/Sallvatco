using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Sallvat.Application.Payments;

namespace Sallvat.Infrastructure.Payments;

internal static class MercadoPagoWebhookSignature
{
    internal static string? Verify(PaymentWebhookRequest request, string secret, DateTimeOffset now)
    {
        if (!Identifier(request.DataId, 64) || !request.DataId.StartsWith("ORD", StringComparison.Ordinal)
            || !Identifier(request.RequestId, 128) || request.Signature.Length > 256)
        {
            return null;
        }

        var parts = request.Signature.Split(',');
        if (parts.Length != 2)
        {
            return null;
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in parts)
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2 || !fields.TryAdd(pair[0], pair[1]))
            {
                return null;
            }
        }

        if (!fields.TryGetValue("ts", out var text) || text.Length is not (10 or 13) || !text.All(char.IsAsciiDigit)
            || !long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp)
            || !fields.TryGetValue("v1", out var signature) || signature.Length != 64 || !signature.All(char.IsAsciiHexDigit))
        {
            return null;
        }

        var signedAt = text.Length == 13 ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp) : DateTimeOffset.FromUnixTimeSeconds(timestamp);
        if ((now - signedAt).Duration() > TimeSpan.FromMinutes(5))
        {
            return null;
        }

        var manifest = Encoding.UTF8.GetBytes($"id:{request.DataId};request-id:{request.RequestId};ts:{text};");
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), manifest);
        return CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(signature))
            ? Convert.ToHexString(SHA256.HashData(manifest)) : null;
    }

    private static bool Identifier(string value, int max) => value.Length > 0 && value.Length <= max
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
