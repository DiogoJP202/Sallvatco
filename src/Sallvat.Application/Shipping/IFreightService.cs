namespace Sallvat.Application.Shipping;

public interface IFreightService
{
    Task<FreightQuoteResult> QuoteAsync(
        FreightQuoteRequest request,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);
}
