namespace Sallvat.Web.Models.Catalog;

public sealed record BodySplashPresentation(
    string Name,
    string ImageSlug,
    string Profile,
    bool Featured = false)
{
    public const int VolumeMl = 200;

    public const decimal DisplayPrice = 45m;
}

public static class BodySplashPresentationCatalog
{
    public static IReadOnlyList<BodySplashPresentation> All { get; } =
    [
        new(
            "Aqua Imagination",
            "aqua-imagination",
            "Refrescante · aquático · masculino"),
        new(
            "Gold Bar",
            "gold-bar",
            "Romântico · clássico · envolvente"),
        new(
            "Sexy Code",
            "sexy-code",
            "Bodyfemme · presença"),
        new(
            "Code 2 Man",
            "code-2-man",
            "Moderno · urbano · energético"),
        new(
            "Sea Salt",
            "sea-salt",
            "Bodyman · para uma aventura",
            Featured: true),
        new(
            "Vanilla Cream",
            "vanilla-cream",
            "Conforto · elegância · unissex",
            Featured: true),
        new(
            "Azurra",
            "azurra",
            "Bodyfemme · liberdade"),
        new(
            "Savage Force",
            "savage-force",
            "Bodyman · presença"),
        new(
            "Golden Freesia",
            "golden-freesia",
            "Romântico · clássico · envolvente",
            Featured: true),
    ];
}

public sealed record BodySplashCollectionPageViewModel(
    IReadOnlyList<BodySplashPresentation> Products);
