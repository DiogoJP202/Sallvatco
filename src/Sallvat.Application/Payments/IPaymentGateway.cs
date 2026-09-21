namespace Sallvat.Application.Payments;

public interface IPaymentGateway
{
    Task<PaymentPreferenceResult> CreatePreferenceAsync(
        PaymentPreferenceRequest request,
        CancellationToken cancellationToken = default);
}
