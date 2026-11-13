using Sallvat.Application.Catalog;

namespace Sallvat.Web.Models.Catalog;

public sealed record HomePageViewModel(
    IReadOnlyList<CatalogProductSummary> FeaturedProducts,
    IReadOnlyList<BodySplashPresentation> BodySplashes,
    IReadOnlyList<string> PublishedFamilies);

public sealed record ProductDetailsPageViewModel(
    CatalogProductDetails Product,
    CatalogVariant SelectedVariant,
    IReadOnlyList<CatalogProductSummary> RelatedProducts,
    string StructuredData);
