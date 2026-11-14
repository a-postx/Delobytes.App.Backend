using System;
using System.Linq;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Contracts.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure.Catalog;

/// <summary>
/// Model metadata of the two barcode sets. The pair of indexes is the whole point of the split: the
/// canonical set stays unique per tenant, which the operator relies on, while the channel's copy is
/// bounded per card only, which is what lets two cards of one channel report the same value. Asserted
/// through the model because EF InMemory does not enforce indexes.
/// </summary>
public class ChannelProductBarcodeConfigurationTests
{
    private const string PerCardUniqueIndex = "IX_ChannelProductBarcodes_TenantId_ChannelProductId_Value";
    private const string ValueLookupIndex = "IX_ChannelProductBarcodes_TenantId_Value";
    private const string CanonicalUniqueIndex = "IX_ProductBarcodes_TenantId_Value";

    [Fact]
    public void ChannelBarcodes_AreUniquePerCardOnly()
    {
        using CatalogDbContext context = BuildContext();

        IIndex index = GetChannelBarcodeIndexes(context)
            .Single(candidate => candidate.GetDatabaseName() == PerCardUniqueIndex);

        index.IsUnique.Should().BeTrue();
        index.Properties.Select(property => property.Name)
            .Should().Equal("TenantId", "ChannelProductId", "Value");
    }

    [Fact]
    public void ChannelBarcodes_HaveANonUniqueValueLookup()
    {
        // The import matches a card's values against every card of the channel, which needs a lookup
        // by value that is explicitly NOT unique: the same value may belong to several cards.
        using CatalogDbContext context = BuildContext();

        IIndex index = GetChannelBarcodeIndexes(context)
            .Single(candidate => candidate.GetDatabaseName() == ValueLookupIndex);

        index.IsUnique.Should().BeFalse();
        index.Properties.Select(property => property.Name)
            .Should().Equal("TenantId", "Value");
    }

    [Fact]
    public void ProductBarcodes_StayUniquePerTenant()
    {
        // The requirement this split must not weaken: one canonical barcode, one product.
        using CatalogDbContext context = BuildContext();

        IEntityType productBarcodeType = context.Model.FindEntityType(typeof(ProductBarcode))!;
        IIndex index = productBarcodeType.GetIndexes()
            .Single(candidate => candidate.GetDatabaseName() == CanonicalUniqueIndex);

        index.IsUnique.Should().BeTrue();
        index.Properties.Select(property => property.Name).Should().Equal("TenantId", "Value");
    }

    [Fact]
    public void ValueIsRequiredAndBoundedLikeTheCanonicalOne()
    {
        using CatalogDbContext context = BuildContext();

        IEntityType entityType = context.Model.FindEntityType(typeof(ChannelProductBarcode))!;

        IProperty value = entityType.FindProperty(nameof(ChannelProductBarcode.Value))!;
        value.IsNullable.Should().BeFalse();
        value.GetMaxLength().Should().Be(100);

        IProperty type = entityType.FindProperty(nameof(ChannelProductBarcode.Type))!;
        type.GetMaxLength().Should().Be(50);
        type.IsNullable.Should().BeTrue("a channel may report a value without naming the barcode system");

        entityType.FindProperty(nameof(ChannelProductBarcode.ChannelProductId))!.IsNullable.Should().BeFalse();
    }

    [Fact]
    public void ChannelBarcode_CascadesWithItsCard()
    {
        // Deleting a link removes its barcodes: they mean nothing without the card they came from.
        using CatalogDbContext context = BuildContext();

        IEntityType entityType = context.Model.FindEntityType(typeof(ChannelProductBarcode))!;
        IForeignKey foreignKey = entityType.GetForeignKeys().Single();

        foreignKey.PrincipalEntityType.ClrType.Should().Be(typeof(ChannelProduct));
        foreignKey.DeleteBehavior.Should().Be(DeleteBehavior.Cascade);
    }

    [Fact]
    public void ChannelProduct_ExposesTheBarcodeCollection()
    {
        using CatalogDbContext context = BuildContext();

        IEntityType channelProductType = context.Model.FindEntityType(typeof(ChannelProduct))!;
        INavigation navigation = channelProductType
            .GetNavigations()
            .Single(candidate => candidate.Name == nameof(ChannelProduct.Barcodes));

        navigation.TargetEntityType.ClrType.Should().Be(typeof(ChannelProductBarcode));
        navigation.IsCollection.Should().BeTrue();
    }

    private static System.Collections.Generic.IEnumerable<IIndex> GetChannelBarcodeIndexes(CatalogDbContext context)
    {
        IEntityType entityType = context.Model.FindEntityType(typeof(ChannelProductBarcode))!;
        return entityType.GetIndexes();
    }

    private static CatalogDbContext BuildContext()
    {
        DbContextOptions<CatalogDbContext> options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("channel-product-barcode-configuration-" + Guid.NewGuid())
            .Options;

        Mock<ITenantContext> tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(context => context.TenantId).Returns(Guid.NewGuid());

        return new CatalogDbContext(options, tenantContextMock.Object);
    }
}
