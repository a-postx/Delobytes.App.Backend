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
/// Model metadata for Product.Code. Uniqueness is asserted here because EF InMemory does not
/// enforce unique indexes: inserting duplicates into an InMemory context proves nothing.
/// </summary>
public class ProductConfigurationTests
{
    private static CatalogDbContext BuildContext()
    {
        DbContextOptions<CatalogDbContext> options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("product-configuration-" + Guid.NewGuid())
            .Options;

        Mock<ITenantContext> tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.TenantId).Returns(Guid.NewGuid());

        return new CatalogDbContext(options, tenantContextMock.Object);
    }

    [Fact]
    public void Code_HasUniqueIndexPerTenant()
    {
        using CatalogDbContext context = BuildContext();

        IEntityType productType = context.Model.FindEntityType(typeof(Product))!;
        IIndex index = productType.GetIndexes()
            .Single(candidate => candidate.GetDatabaseName() == "IX_Products_TenantId_Code");

        index.IsUnique.Should().BeTrue();
        index.Properties.Select(property => property.Name)
            .Should().Equal("TenantId", nameof(Product.Code));
    }

    [Fact]
    public void Code_IsRequiredAndLimitedToFiftyCharacters()
    {
        using CatalogDbContext context = BuildContext();

        IEntityType productType = context.Model.FindEntityType(typeof(Product))!;
        IProperty code = productType.FindProperty(nameof(Product.Code))!;

        code.IsNullable.Should().BeFalse();
        code.GetMaxLength().Should().Be(50);
    }

    [Fact]
    public void Sku_KeepsItsExistingUniqueIndex()
    {
        // The code index must not replace the SKU one: both are enforced, and the conflict
        // translator still maps the SKU index to catalog.product.sku_conflict.
        using CatalogDbContext context = BuildContext();

        IEntityType productType = context.Model.FindEntityType(typeof(Product))!;
        IIndex index = productType.GetIndexes()
            .Single(candidate => candidate.GetDatabaseName() == "IX_Products_TenantId_Sku");

        index.IsUnique.Should().BeTrue();
        index.Properties.Select(property => property.Name)
            .Should().Equal("TenantId", nameof(Product.Sku));
    }
}
