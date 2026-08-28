using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Sallvat.Web.Observability;

public static class HealthCheckResponseWriter
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(
        HttpContext httpContext,
        HealthReport healthReport)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(healthReport);

        httpContext.Response.ContentType = "application/json; charset=utf-8";

        return httpContext.Response.WriteAsJsonAsync(
            new HealthResponse(healthReport.Status.ToString()),
            SerializerOptions,
            httpContext.RequestAborted);
    }

    private sealed record HealthResponse(string Status);
}
