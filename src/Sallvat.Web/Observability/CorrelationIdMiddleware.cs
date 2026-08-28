using Microsoft.Extensions.Options;
using Sallvat.Web.Configuration;

namespace Sallvat.Web.Observability;

public sealed class CorrelationIdMiddleware(
    RequestDelegate next,
    ILogger<CorrelationIdMiddleware> logger,
    IOptions<OperationalOptions> operationalOptions)
{
    public const string HeaderName = "X-Correlation-ID";

    private readonly int maxLength =
        operationalOptions.Value.CorrelationIdMaxLength;

    public async Task InvokeAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var correlationId = ReadValidCorrelationId(httpContext.Request.Headers)
            ?? Guid.NewGuid().ToString("N");

        httpContext.TraceIdentifier = correlationId;
        httpContext.Response.Headers[HeaderName] = correlationId;

        using (logger.BeginScope(
                   new Dictionary<string, object>
                   {
                       ["CorrelationId"] = correlationId,
                   }))
        {
            await next(httpContext);
        }
    }

    private string? ReadValidCorrelationId(IHeaderDictionary headers)
    {
        var values = headers[HeaderName];

        if (values.Count != 1)
        {
            return null;
        }

        var candidate = values[0];

        return IsValid(candidate) ? candidate : null;
    }

    private bool IsValid(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)
            || candidate.Length > maxLength)
        {
            return false;
        }

        foreach (var character in candidate)
        {
            if (!IsAllowedCharacter(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAllowedCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character)
        || character is '-' or '_' or '.';
}

public static class CorrelationIdMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(
        this IApplicationBuilder applicationBuilder)
    {
        ArgumentNullException.ThrowIfNull(applicationBuilder);

        return applicationBuilder.UseMiddleware<CorrelationIdMiddleware>();
    }
}
