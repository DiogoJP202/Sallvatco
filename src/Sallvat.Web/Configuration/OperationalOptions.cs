namespace Sallvat.Web.Configuration;

public sealed class OperationalOptions
{
    public const string SectionName = "Operational";

    public string ServiceName { get; init; } = string.Empty;

    public int CorrelationIdMaxLength { get; init; }
}
