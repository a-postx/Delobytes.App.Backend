using Delobytes.App.Backend.Catalog.Contracts.Lookup;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Catalog.Infrastructure.Lookup;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Contracts.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure.Catalog;

/// <summary>
/// ProductSkuLookup, the read contract other modules label their rows with. The contract:
/// requested identifiers resolve whatever the product status, an unknown identifier is absent
/// rather than an error, and the tenant filter of the catalogue context keeps the result inside
/// one tenant.
/// </summary>
public class ProductSkuLookupTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task GetSkusAsync_ResolvesRequestedProductsWhateverTheirStatus()
    {
        // Arrange: an archived product must still label a historical report row.
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName, _tenantId);

        Product active = Product("SKU-ACTIVE", ProductStatus.Active);
        Product archived = Product("SKU-ARCHIVED", ProductStatus.Archived);
        Product other = Product("SKU-OTHER", ProductStatus.Active);

        setupContext.Products.AddRange(active, archived, other);
        await setupContext.SaveChangesAsync();

        ProductSkuLookup lookup = new ProductSkuLookup(BuildContext(databaseName, _tenantId));

        // Act
        IReadOnlyDictionary<Guid, string> skus = await lookup.GetSkusAsync(
            new[] { active.Id, archived.Id },
            CancellationToken.None);

        // Assert
        skus.Should().HaveCount(2);
        skus[active.Id].Should().Be("SKU-ACTIVE");
        skus[archived.Id].Should().Be("SKU-ARCHIVED");
    }

    [Fact]
    public async Task GetSkusAsync_UnknownId_IsAbsent()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName, _tenantId);

        Product product = Product("SKU-1", ProductStatus.Active);
        setupContext.Products.Add(product);
        await setupContext.SaveChangesAsync();

        ProductSkuLookup lookup = new ProductSkuLookup(BuildContext(databaseName, _tenantId));

        // Act
        IReadOnlyDictionary<Guid, string> skus = await lookup.GetSkusAsync(
            new[] { product.Id, Guid.NewGuid() },
            CancellationToken.None);

        // Assert
        skus.Should().ContainSingle().Which.Key.Should().Be(product.Id);
    }

    [Fact]
    public async Task GetSkusAsync_EmptyRequest_ReturnsEmpty()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        ProductSkuLookup lookup = new ProductSkuLookup(BuildContext(databaseName, _tenantId));

        // Act
        IReadOnlyDictionary<Guid, string> skus = await lookup.GetSkusAsync(
            Array.Empty<Guid>(),
            CancellationToken.None);

        // Assert
        skus.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSkusAsync_ProductOfAnotherTenant_IsAbsent()
    {
        // Arrange: the lookup has no tenant parameter, so the catalogue filter is the only guard.
        string databaseName = Guid.NewGuid().ToString();
        Guid otherTenantId = Guid.NewGuid();
        CatalogDbContext setupContext = BuildContext(databaseName, otherTenantId);

        Product foreign = Product("SKU-FOREIGN", ProductStatus.Active);
        setupContext.Products.Add(foreign);
        await setupContext.SaveChangesAsync();

        ProductSkuLookup lookup = new ProductSkuLookup(BuildContext(databaseName, _tenantId));

        // Act
        IReadOnlyDictionary<Guid, string> skus = await lookup.GetSkusAsync(
            new[] { foreign.Id },
            CancellationToken.None);

        // Assert
        skus.Should().BeEmpty();
    }

    private static Product Product(string sku, ProductStatus status)
    {
        return new Product
        {
            Id = Guid.NewGuid(),
            Code = "C-" + sku,
            Sku = sku,
            Name = sku,
            Status = status,
            CreatedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
    }

    private static CatalogDbContext BuildContext(string databaseName, Guid tenantId)
    {
        DbContextOptions<CatalogDbContext> options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        Mock<ITenantContext> tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.TenantId).Returns(tenantId);

        return new CatalogDbContext(options, tenantContextMock.Object);
    }
}
