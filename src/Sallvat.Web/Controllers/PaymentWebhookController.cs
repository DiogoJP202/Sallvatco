using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sallvat.Application.Payments;

namespace Sallvat.Web.Controllers;

[AllowAnonymous]
[IgnoreAntiforgeryToken]
[EnableRateLimiting("PaymentWebhook")]
public sealed class PaymentWebhookController(IPaymentWebhookService service) : ControllerBase
{
    [HttpPost("integracoes/mercado-pago/webhook")]
    [RequestSizeLimit(16_384)]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        if (!Request.IsHttps || !Request.HasJsonContentType()
            || Request.Query["data.id"].Count != 1 || Request.Headers["x-request-id"].Count != 1
            || Request.Headers["x-signature"].Count != 1)
        {
            return BadRequest();
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(18));
        try
        {
            // Independently enforce the limit for chunked bodies and servers without size-limit features.
            var buffer = new byte[16_385];
            var count = 0;
            while (count < buffer.Length)
            {
                var read = await Request.Body.ReadAsync(buffer.AsMemory(count), timeout.Token);
                if (read == 0)
                {
                    break;
                }

                count += read;
            }

            if (count > 16_384)
            {
                return StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            var result = await service.HandleAsync(new(Request.Query["data.id"].ToString(), Request.Headers["x-request-id"].ToString(),
                Request.Headers["x-signature"].ToString(), buffer[..count]), timeout.Token);
            return result switch
            {
                PaymentWebhookResult.Accepted => Ok(),
                PaymentWebhookResult.Disabled => NotFound(),
                PaymentWebhookResult.Invalid => BadRequest(),
                PaymentWebhookResult.Unauthorized => Unauthorized(),
                _ => StatusCode(StatusCodes.Status503ServiceUnavailable),
            };
        }
        catch (OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }
}
