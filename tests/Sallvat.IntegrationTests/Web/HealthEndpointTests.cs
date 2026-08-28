using System.Net;
using System.Text.Json;
using Sallvat.Web.Observability;

namespace Sallvat.IntegrationTests.Web;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task LiveReturnsOnlyAggregateStatusAndCorrelationId()
    {
        await using var application = new SallvatWebApplicationFactory();
        using var client = application.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/health/live");
        request.Headers.Add(
            CorrelationIdMiddleware.HeaderName,
            "test-correlation-123");

        using var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "test-correlation-123",
            response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
        Assert.Equal("Healthy", document.RootElement.GetProperty("status").GetString());
        Assert.Single(document.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task InvalidExternalCorrelationIdIsReplaced()
    {
        await using var application = new SallvatWebApplicationFactory();
        using var client = application.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/health/live");
        request.Headers.TryAddWithoutValidation(
            CorrelationIdMiddleware.HeaderName,
            "invalid correlation id");

        using var response = await client.SendAsync(request);
        var correlationId = response.Headers
            .GetValues(CorrelationIdMiddleware.HeaderName)
            .Single();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual("invalid correlation id", correlationId);
        Assert.Equal(32, correlationId.Length);
        Assert.All(correlationId, character =>
            Assert.True(char.IsAsciiHexDigit(character)));
    }

    [Fact]
    public async Task ReadyDoesNotExposeFailedDependencyDetails()
    {
        await using var application = new SallvatWebApplicationFactory();
        using var client = application.CreateClient();

        using var response = await client.GetAsync("/health/ready");
        var content = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(content);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", document.RootElement.GetProperty("status").GetString());
        Assert.Single(document.RootElement.EnumerateObject());
        Assert.DoesNotContain("postgresql", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("127.0.0.1", content, StringComparison.Ordinal);
    }
}
