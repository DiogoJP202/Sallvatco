using System.Net;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sallvat.Web.Controllers;
using Sallvat.Web.Models;
using Sallvat.Web.Observability;

namespace Sallvat.IntegrationTests.Web;

public sealed class ErrorControllerTests
{
    [Fact]
    public void ErrorResponseContainsSupportIdButNotExceptionDetails()
    {
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "support-correlation-123",
        };
        httpContext.Features.Set<IExceptionHandlerPathFeature>(
            new ExceptionHandlerFeature
            {
                Error = new InvalidOperationException(
                    "sensitive internal detail"),
                Path = "/checkout",
            });
        var controller = new ErrorController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
            },
        };

        var result = Assert.IsType<ViewResult>(controller.Index());
        var model = Assert.IsType<ErrorViewModel>(result.Model);

        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);
        Assert.Equal("support-correlation-123", model.CorrelationId);
        Assert.DoesNotContain(
            "sensitive internal detail",
            model.CorrelationId,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DirectNavigationToErrorRouteReturnsNotFound()
    {
        var controller = new ErrorController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

        Assert.IsType<NotFoundResult>(controller.Index());
    }

    [Fact]
    public async Task UnexpectedFailureReturnsSafeHtmlPage()
    {
        await using var application = new SallvatWebApplicationFactory();
        using var client = application.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/__tests/failure");
        request.Headers.Accept.ParseAdd("text/html");
        request.Headers.Add(
            CorrelationIdMiddleware.HeaderName,
            "support-correlation-456");

        using var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("support-correlation-456", content, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "sensitive internal detail",
            content,
            StringComparison.Ordinal);
        Assert.DoesNotContain("stack", content, StringComparison.OrdinalIgnoreCase);
    }
}
