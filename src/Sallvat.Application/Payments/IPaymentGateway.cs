namespace Sallvat.Application.Payments;

public interface IPaymentGateway
{
    Task<PaymentRefundSubmitResult> RefundOrderAsync(PaymentRefundSubmit request, CancellationToken cancellationToken = default);

    Task<PaymentOrderQueryResult> GetOrderAsync(
        PaymentOrderQuery request,
        CancellationToken cancellationToken = default);

    Task<PaymentOrderResult> CreateOrderAsync(
        PaymentOrderRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentPreferenceResult> CreatePreferenceAsync(
        PaymentPreferenceRequest request,
        CancellationToken cancellationToken = default);
}
