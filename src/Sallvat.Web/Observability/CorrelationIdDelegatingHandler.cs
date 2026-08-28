namespace Sallvat.Web.Observability;

public sealed class CorrelationIdDelegatingHandler(
    IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var correlationId = httpContextAccessor.HttpContext?.TraceIdentifier;

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            request.Headers.Remove(CorrelationIdMiddleware.HeaderName);
            request.Headers.TryAddWithoutValidation(
                CorrelationIdMiddleware.HeaderName,
                correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
