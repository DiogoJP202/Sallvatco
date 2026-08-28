using Sallvat.Web.Observability;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Sallvat.IntegrationTests.Web;

public sealed class SensitiveDataEnricherTests
{
    [Fact]
    public void SensitivePropertiesAreRedactedAndRequestPathIsRemoved()
    {
        var sink = new RecordingSink();

        using var logger = new LoggerConfiguration()
            .Enrich.With(new SensitiveDataEnricher())
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information(
            "Request {RequestPath} by {Email} for order {OrderId} with {@Payload}",
            "/conta/reset?token=secret",
            "customer@example.com",
            "order-123",
            new
            {
                Phone = "11999999999",
                Status = "accepted",
            });

        var logEvent = Assert.IsType<LogEvent>(sink.LastEvent);
        var email = Assert.IsType<ScalarValue>(logEvent.Properties["Email"]);
        var orderId = Assert.IsType<ScalarValue>(logEvent.Properties["OrderId"]);
        var payload = Assert.IsType<StructureValue>(logEvent.Properties["Payload"]);
        var phone = Assert.IsType<ScalarValue>(
            payload.Properties.Single(property => property.Name == "Phone").Value);
        var status = Assert.IsType<ScalarValue>(
            payload.Properties.Single(property => property.Name == "Status").Value);

        Assert.Equal("[REDACTED]", email.Value);
        Assert.Equal("[REDACTED]", phone.Value);
        Assert.Equal("order-123", orderId.Value);
        Assert.Equal("accepted", status.Value);
        Assert.DoesNotContain("RequestPath", logEvent.Properties.Keys);
    }

    private sealed class RecordingSink : ILogEventSink
    {
        public LogEvent? LastEvent { get; private set; }

        public void Emit(LogEvent logEvent)
        {
            LastEvent = logEvent;
        }
    }
}
