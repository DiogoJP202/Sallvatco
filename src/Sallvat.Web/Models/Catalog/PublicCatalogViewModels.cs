using Sallvat.Application.Catalog;

namespace Sallvat.Web.Models.Catalog;

public sealed record HomePageViewModel(
    IReadOnlyList<CatalogProductSummary> FeaturedProducts);

public sealed record ProductDetailsPageViewModel(
    CatalogProductDetails Product,
    CatalogVariant SelectedVariant,
    string StructuredData);
