namespace Sallvat.Application.Shipping;

public static class FreightPackagingPolicy
{
    public const string PendingMessage =
        "A cotação para mais de uma unidade depende da definição da caixa de envio. " +
        "Sua sacola foi mantida; nenhum frete ou pagamento foi confirmado.";

    public static bool RequiresConsolidatedPackage(IReadOnlyList<FreightQuoteItem> items) =>
        items.Count > 1 || items.Any(item => item.Quantity > 1);
}
