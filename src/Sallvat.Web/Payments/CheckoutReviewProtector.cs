using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Sallvat.Application.Carts;
using Sallvat.Application.Checkout;
using Sallvat.Application.Time;

namespace Sallvat.Web.Payments;

// Encrypted, owner-bound, short-lived review; never put this token or its PII in URLs/logs.
public sealed class CheckoutReviewProtector(IDataProtectionProvider provider, IClock clock)
{
    public string Protect(CheckoutConfirmation confirmation, CartOwner owner) =>
        Protector(owner).Protect(JsonSerializer.Serialize(confirmation));

    public CheckoutConfirmation? Read(string? token, CartOwner owner)
    {
        if (token is not { Length: > 0 and <= 24_000 })
        {
            return null;
        }

        try
        {
            var confirmation = JsonSerializer.Deserialize<CheckoutConfirmation>(Protector(owner).Unprotect(token));
            return confirmation is not null && confirmation.AttemptId != Guid.Empty
                && confirmation.ExpiresAtUtc > clock.UtcNow ? confirmation : null;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private IDataProtector Protector(CartOwner owner)
    {
        var identity = owner.ApplicationUserId is Guid id && id != Guid.Empty && owner.GuestToken is null
            ? "customer:" + id.ToString("D")
            : owner.ApplicationUserId is null && owner.GuestToken is { Length: 43 } token
                ? "guest:" + token
                : throw new ArgumentException("Checkout owner is required.", nameof(owner));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return provider.CreateProtector("Sallvat.Checkout.Review.v1", hash);
    }
}
