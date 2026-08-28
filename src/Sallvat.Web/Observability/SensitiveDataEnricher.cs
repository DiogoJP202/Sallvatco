using Serilog.Core;
using Serilog.Events;

namespace Sallvat.Web.Observability;

public sealed class SensitiveDataEnricher : ILogEventEnricher
{
    private const string RedactedValue = "[REDACTED]";

    private static readonly string[] SensitivePropertyNameFragments =
    [
        "address",
        "antiforgery",
        "authorization",
        "card",
        "connectionstring",
        "cookie",
        "cpf",
        "cvv",
        "email",
        "password",
        "phone",
        "reseturl",
        "secret",
        "signature",
        "token",
    ];

    public void Enrich(
        LogEvent logEvent,
        ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        logEvent.RemovePropertyIfPresent("RequestPath");

        foreach (var propertyName in logEvent.Properties.Keys.ToArray())
        {
            logEvent.AddOrUpdateProperty(
                new LogEventProperty(
                    propertyName,
                    Sanitize(
                        propertyName,
                        logEvent.Properties[propertyName])));
        }
    }

    private static LogEventPropertyValue Sanitize(
        string propertyName,
        LogEventPropertyValue propertyValue)
    {
        if (IsSensitive(propertyName))
        {
            return new ScalarValue(RedactedValue);
        }

        return propertyValue switch
        {
            StructureValue structure => new StructureValue(
                structure.Properties.Select(property =>
                    new LogEventProperty(
                        property.Name,
                        Sanitize(property.Name, property.Value))),
                structure.TypeTag),
            SequenceValue sequence => new SequenceValue(
                sequence.Elements.Select(value => Sanitize(string.Empty, value))),
            DictionaryValue dictionary => new DictionaryValue(
                dictionary.Elements.Select(element =>
                    new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                        element.Key,
                        Sanitize(
                            element.Key.Value?.ToString() ?? string.Empty,
                            element.Value)))),
            _ => propertyValue,
        };
    }

    private static bool IsSensitive(string propertyName) =>
        SensitivePropertyNameFragments.Any(fragment =>
            propertyName.Contains(
                fragment,
                StringComparison.OrdinalIgnoreCase));
}
