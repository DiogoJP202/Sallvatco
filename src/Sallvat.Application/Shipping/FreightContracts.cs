namespace Sallvat.Application.Shipping;

public sealed record FreightQuoteItem(
    string Reference,
    int Quantity,
    decimal UnitPrice,
    string Currency,
    decimal WeightKg,
    decimal HeightCm,
    decimal WidthCm,
    decimal LengthCm);

public sealed record FreightQuoteRequest(
    string DestinationPostalCode,
    IReadOnlyList<FreightQuoteItem> Items);

public sealed record FreightQuoteOption(
    string QuoteId,
    string Carrier,
    string Service,
    decimal Price,
    string Currency,
    int MinimumBusinessDays,
    int MaximumBusinessDays,
    DateTimeOffset QuotedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public enum FreightQuoteStatus
{
    Succeeded,
    Invalid,
    ConfigurationMissing,
    AuthenticationFailure,
    RateLimited,
    Unavailable,
}

public sealed record FreightQuoteResult(
    FreightQuoteStatus Status,
    IReadOnlyList<FreightQuoteOption> Options,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded =>
        Status == FreightQuoteStatus.Succeeded && Options.Count > 0;

    public static FreightQuoteResult Success(
        IReadOnlyList<FreightQuoteOption> options) =>
        new(FreightQuoteStatus.Succeeded, options, []);

    public static FreightQuoteResult Failure(
        FreightQuoteStatus status,
        params string[] errors) =>
        new(status, [], errors);
}

public enum FreightSelectionStatus
{
    Succeeded,
    Invalid,
    PriceChanged,
    Unavailable,
}

public sealed record FreightSelectionResult(
    FreightSelectionStatus Status,
    Orders.CheckoutShippingSnapshot? Snapshot,
    FreightQuoteOption? CurrentOption,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded =>
        Status == FreightSelectionStatus.Succeeded && Snapshot is not null;

    public static FreightSelectionResult Success(
        Orders.CheckoutShippingSnapshot snapshot,
        FreightQuoteOption option) =>
        new(FreightSelectionStatus.Succeeded, snapshot, option, []);

    public static FreightSelectionResult Failure(
        FreightSelectionStatus status,
        FreightQuoteOption? currentOption,
        params string[] errors) =>
        new(status, null, currentOption, errors);
}
