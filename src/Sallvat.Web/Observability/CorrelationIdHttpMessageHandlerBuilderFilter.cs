using Microsoft.Extensions.Http;

namespace Sallvat.Web.Observability;

public sealed class CorrelationIdHttpMessageHandlerBuilderFilter :
    IHttpMessageHandlerBuilderFilter
{
    public Action<HttpMessageHandlerBuilder> Configure(
        Action<HttpMessageHandlerBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return builder =>
        {
            next(builder);
            builder.AdditionalHandlers.Insert(
                0,
                builder.Services.GetRequiredService<
                    CorrelationIdDelegatingHandler>());
        };
    }
}
