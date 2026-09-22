namespace Sallvat.Application.Payments;

public interface IPaymentGateway
{
    Task<PaymentOrderResult> CreateOrderAsync(
        PaymentOrderRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentPreferenceResult> CreatePreferenceAsync(
        PaymentPreferenceRequest request,
        CancellationToken cancellationToken = default);
}
