using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;
using Delobytes.App.Backend.Contracts.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Delobytes.App.Backend.Tests.Infrastructure.Catalog;

public class ProductCostSnapshotRepositoryTests
{
    [Fact]
    public async Task GetExistingProductIdsAsync_ReturnsOnlyMatchingProductDateAndReason()
    {
        Guid tenantId = Guid.NewGuid();
        Guid matchingProductId = Guid.NewGuid();
        Guid otherProductId = Guid.NewGuid();
        DateOnly asOfDate = new DateOnly(2026, 6, 1);
        DbContextOptions<CatalogDbContext> options = BuildOptions();

        using (CatalogDbContext context = BuildContext(options, tenantId))
        {
            Product product = BuildProduct(matchingProductId, "MATCH");
            Product otherProduct = BuildProduct(otherProductId, "OTHER");
            context.Products.AddRange(product, otherProduct);
            context.ProductCostSnapshots.AddRange(
                BuildSnapshot(matchingProductId, asOfDate, "TestTrigger"),
                BuildSnapshot(matchingProductId, asOfDate.AddDays(-1), "TestTrigger"),
                BuildSnapshot(matchingProductId, asOfDate, "BomChanged"),
                BuildSnapshot(otherProductId, asOfDate, "TestTrigger"));
            await context.SaveChangesAsync();
        }

        using CatalogDbContext readContext = BuildContext(options, tenantId);
        ProductCostSnapshotRepository repository = new ProductCostSnapshotRepository(readContext);

        IReadOnlySet<Guid> result = await repository.GetExistingProductIdsAsync(
            new[] { matchingProductId, otherProductId },
            asOfDate,
            "TestTrigger",
            CancellationToken.None);

        result.Should().Equal(matchingProductId, otherProductId);
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsItemsOrderedByDateAndCalculatedAtWithPagination()
    {
        Guid tenantId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        DateOnly firstDate = new DateOnly(2026, 1, 1);
        DateOnly secondDate = new DateOnly(2026, 2, 1);
        DbContextOptions<CatalogDbContext> options = BuildOptions();

        using (CatalogDbContext context = BuildContext(options, tenantId))
        {
            context.Products.Add(BuildProduct(productId, "PRODUCT"));
            context.ProductCostSnapshots.AddRange(
                BuildSnapshot(productId, firstDate, "TestTrigger", new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero), 10m),
                BuildSnapshot(productId, secondDate, "TestTrigger", new DateTimeOffset(2026, 2, 1, 8, 0, 0, TimeSpan.Zero), 20m),
                BuildSnapshot(productId, secondDate, "BomChanged", new DateTimeOffset(2026, 2, 1, 9, 0, 0, TimeSpan.Zero), 30m));
            await context.SaveChangesAsync();
        }

        using CatalogDbContext readContext = BuildContext(options, tenantId);
        ProductCostSnapshotRepository repository = new ProductCostSnapshotRepository(readContext);

        (int totalCount, IReadOnlyList<ProductCostSnapshot> items) = await repository.GetHistoryAsync(
            productId,
            1,
            1,
            CancellationToken.None);

        totalCount.Should().Be(3);
        items.Should().ContainSingle();
        items[0].TotalCost.Should().Be(20m);
    }

    private static DbContextOptions<CatalogDbContext> BuildOptions()
    {
        return new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("product-cost-snapshot-tests-" + Guid.NewGuid())
            .Options;
    }

    private static CatalogDbContext BuildContext(
        DbContextOptions<CatalogDbContext> options,
        Guid tenantId)
    {
        Mock<ITenantContext> tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(context => context.TenantId).Returns(tenantId);
        return new CatalogDbContext(options, tenantContextMock.Object);
    }

    private static Product BuildProduct(Guid id, string sku)
    {
        return new Product
        {
            Id = id,
            Sku = sku,
            Name = sku,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
    }

    private static ProductCostSnapshot BuildSnapshot(
        Guid productId,
        DateOnly asOfDate,
        string triggerReason,
        DateTimeOffset? calculatedAt = null,
        decimal totalCost = 100m)
    {
        return new ProductCostSnapshot
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            AsOfDate = asOfDate,
            MaterialCost = totalCost,
            LogisticsCost = 0m,
            PackagingCost = 0m,
            LaborCost = 0m,
            TotalCost = totalCost,
            IsComplete = true,
            LinesSnapshotJson = "[]",
            TriggerReason = triggerReason,
            CalculatedAt = calculatedAt ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
    }
}
