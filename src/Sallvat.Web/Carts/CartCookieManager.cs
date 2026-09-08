using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace Sallvat.Web.Carts;

public sealed class CartCookieManager(IWebHostEnvironment environment)
{
    private const int TokenByteLength = 32;
    private const int EncodedTokenLength = 43;
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);
    private readonly string cookieName =
        $"Sallvat.Cart.{environment.EnvironmentName}";

    public string? ReadToken(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(
                cookieName,
                out var token))
        {
            return null;
        }

        if (IsValid(token))
        {
            return token;
        }

        Delete(context);
        return null;
    }

    public string GetOrCreateToken(HttpContext context)
    {
        var current = ReadToken(context);
        if (current is not null)
        {
            return current;
        }

        var token = WebEncoders.Base64UrlEncode(
            RandomNumberGenerator.GetBytes(TokenByteLength));
        context.Response.Cookies.Append(
            cookieName,
            token,
            CreateCookieOptions(context));

        return token;
    }

    public void Delete(HttpContext context) =>
        context.Response.Cookies.Delete(
            cookieName,
            CreateCookieOptions(context));

    private CookieOptions CreateCookieOptions(HttpContext context) =>
        new()
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = !environment.IsDevelopment() || context.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.Add(Lifetime),
            Path = "/",
        };

    private static bool IsValid(string token) =>
        token.Length == EncodedTokenLength
        && token.All(character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '-' or '_');
}
