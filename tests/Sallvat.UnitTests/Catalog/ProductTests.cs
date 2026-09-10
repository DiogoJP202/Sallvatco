using Sallvat.Domain.Catalog;

namespace Sallvat.UnitTests.Catalog;

public sealed class ProductTests
{
    private static readonly DateTimeOffset Timestamp =
        new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SlugIsNormalizedWithoutDiacritics()
    {
        var product = CreateProduct("  Águas de Verão  ");

        Assert.Equal("aguas-de-verao", product.Slug);
        Assert.Equal(ProductStatus.Draft, product.Status);
    }

    [Fact]
    public void PublicationRequiresImageVariantAndEditorialContent()
    {
        var product = new Product(
            "Perfume",
            "perfume",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            Timestamp);

        Assert.Throws<InvalidOperationException>(() =>
            product.Publish(false, false, Timestamp));
        Assert.Throws<InvalidOperationException>(() =>
            product.Publish(true, true, Timestamp));
    }

    [Fact]
    public void CompleteProductCanBePublishedAndFeatured()
    {
        var product = CreateProduct("perfume-autoral");

        product.Publish(true, true, Timestamp.AddMinutes(1));
        product.SetFeatured(true, Timestamp.AddMinutes(2));

        Assert.Equal(ProductStatus.Published, product.Status);
        Assert.True(product.IsFeatured);
        Assert.Equal(Timestamp.AddMinutes(1), product.PublishedAtUtc);
    }

    [Fact]
    public void PublishedProductCannotLoseRequiredEditorialContent()
    {
        var product = CreateProduct("perfume-autoral");
        product.Publish(true, true, Timestamp.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => product.UpdateDetails(
            "Perfume Autoral",
            "perfume-autoral",
            null,
            "Descrição completa.",
            "Amadeirado",
            "Bergamota",
            "Íris",
            "Sândalo",
            "Eau de parfum",
            "Moderada",
            "8 horas",
            "Noite",
            "Outono",
            "Noturno",
            Timestamp.AddMinutes(2)));
    }

    [Fact]
    public void VariantRejectsBalanceBelowReservedAndNoOpAdjustment()
    {
        var variant = CreateVariant();

        Assert.Throws<InvalidOperationException>(() =>
            variant.AdjustOnHand(0, Timestamp.AddMinutes(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            variant.AdjustOnHand(-1, Timestamp.AddMinutes(1)));
    }

    [Fact]
    public void ReservationUsesAvailableBalanceAndChangesConcurrencyToken()
    {
        var variant = CreateVariant();
        variant.AdjustOnHand(2, Timestamp.AddMinutes(1));
        var previousVersion = variant.ConcurrencyVersion;

        Assert.True(variant.Reserve(2, Timestamp.AddMinutes(2)));
        Assert.Equal(0, variant.Available);
        Assert.NotEqual(previousVersion, variant.ConcurrencyVersion);
        Assert.False(variant.Reserve(1, Timestamp.AddMinutes(3)));
        Assert.Equal(2, variant.Reserved);
    }

    [Fact]
    public void ReservedBalanceCanBeReleasedOrConsumed()
    {
        var variant = CreateVariant();
        variant.AdjustOnHand(4, Timestamp.AddMinutes(1));
        variant.Reserve(3, Timestamp.AddMinutes(2));

        variant.ReleaseReservation(1, Timestamp.AddMinutes(3));
        Assert.Equal(4, variant.OnHand);
        Assert.Equal(2, variant.Reserved);

        variant.ConsumeReservation(2, Timestamp.AddMinutes(4));
        Assert.Equal(2, variant.OnHand);
        Assert.Equal(0, variant.Reserved);
        Assert.Throws<InvalidOperationException>(() =>
            variant.ReleaseReservation(1, Timestamp.AddMinutes(5)));
    }

    [Theory]
    [InlineData("../secret.webp")]
    [InlineData("/absolute/image.webp")]
    [InlineData("\\absolute\\image.webp")]
    public void ImageRejectsUnsafeStorageKeys(string storageKey)
    {
        Assert.Throws<ArgumentException>(() => new ProductImage(
            1,
            storageKey,
            "Perfume",
            1200,
            1500,
            0,
            true,
            Timestamp));
    }

    private static Product CreateProduct(string slug) =>
        new(
            "Perfume Autoral",
            slug,
            "Uma fragrância autoral.",
            "Descrição completa da fragrância.",
            "Amadeirado",
            "Bergamota",
            "Íris",
            "Sândalo",
            "Eau de parfum",
            "Moderada",
            "8 horas",
            "Noite",
            "Outono",
            "Noturno",
            Timestamp);

    private static ProductVariant CreateVariant() =>
        new(
            1,
            "SAL-001",
            50,
            299.90m,
            0.4m,
            12m,
            8m,
            8m,
            Timestamp);
}
