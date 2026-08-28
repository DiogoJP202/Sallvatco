using Microsoft.AspNetCore.Http;
using Sallvat.Web.Observability;

namespace Sallvat.IntegrationTests.Web;

public sealed class CorrelationIdDelegatingHandlerTests
{
    [Fact]
    public async Task CurrentCorrelationIdReplacesHeaderOnOutgoingRequest()
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                TraceIdentifier = "current-correlation-123",
            },
        };
        var recordingHandler = new RecordingHandler();
        using var handler = new CorrelationIdDelegatingHandler(
            httpContextAccessor)
        {
            InnerHandler = recordingHandler,
        };
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://example.invalid/resource");
        request.Headers.Add(
            CorrelationIdMiddleware.HeaderName,
            "stale-correlation");

        using var response = await client.SendAsync(request);

        Assert.Equal(
            "current-correlation-123",
            recordingHandler.CorrelationId);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? CorrelationId { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CorrelationId = request.Headers
                .GetValues(CorrelationIdMiddleware.HeaderName)
                .Single();

            return Task.FromResult(new HttpResponseMessage());
        }
    }
}
