using Microsoft.AspNetCore.Mvc;

namespace Sallvat.IntegrationTests.Web;

[ApiController]
[Route("/__tests/failure")]
public sealed class FailureController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        _ = HttpContext.TraceIdentifier;

        throw new InvalidOperationException("sensitive internal detail");
    }
}
