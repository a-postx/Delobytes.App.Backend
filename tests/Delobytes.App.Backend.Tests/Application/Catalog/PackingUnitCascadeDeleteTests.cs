using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Contracts.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog;

/// <summary>
/// PackingUnit is an owned child of Product and carries no meaning on its own. EF cascades
/// a dependent on the principal's delete as soon as the relationship is required, so the
/// guarantee this test actually pins is that PackingUnit.ProductId stays non-nullable:
/// making it Guid? turns the delete into an orphan row instead of a removal.
/// Verified by mutation — the test fails on a nullable FK and passes on ClientSetNull.
/// </summary>
public class PackingUnitCascadeDeleteTests
{
    private static DbContextOptions<CatalogDbContext> BuildOptions()
    {
        return new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("packing-unit-cascade-" + Guid.NewGuid())
            .Options;
    }

    private static Mock<ITenantContext> TenantMock(Guid tenantId)
    {
        Mock<ITenantContext> mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        return mock;
    }

    private static Product BuildProduct()
    {
        return new Product
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-" + Guid.NewGuid().ToString("N").Substring(0, 8),
            Name = "Товар"
        };
    }

    private static PackingUnit BuildPackingUnit(Guid productId, string name, decimal lengthCm)
    {
        return new PackingUnit
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            Name = name,
            LengthCm = lengthCm,
            WidthCm = 30m,
            HeightCm = 20m,
            WeightKg = 1.5m,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    [Fact]
    public async Task DeletingProduct_RemovesItsPackingUnits_AndKeepsOtherProductsIntact()
    {
        // Arrange
        DbContextOptions<CatalogDbContext> options = BuildOptions();
        Mock<ITenantContext> tenantMock = TenantMock(Guid.NewGuid());

        Product doomedProduct = BuildProduct();
        PackingUnit doomedFirst = BuildPackingUnit(doomedProduct.Id, "Коробка", 40m);
        PackingUnit doomedSecond = BuildPackingUnit(doomedProduct.Id, "Пакет", 25m);

        Product survivingProduct = BuildProduct();
        PackingUnit survivingUnit = BuildPackingUnit(survivingProduct.Id, "Коробка", 60m);

        using (CatalogDbContext ctx = new CatalogDbContext(options, tenantMock.Object))
        {
            ctx.Products.AddRange(doomedProduct, survivingProduct);
            ctx.PackingUnits.AddRange(doomedFirst, doomedSecond, survivingUnit);
            await ctx.SaveChangesAsync();
        }

        // Act
        using (CatalogDbContext ctx = new CatalogDbContext(options, tenantMock.Object))
        {
            // The in-memory provider applies the cascade to tracked dependents only,
            // so the children have to be loaded with the product for the delete to reach them.
            Product loaded = await ctx.Products
                .Include(p => p.PackingUnits)
                .SingleAsync(p => p.Id == doomedProduct.Id);

            loaded.PackingUnits.Should().HaveCount(2);

            ctx.Products.Remove(loaded);
            await ctx.SaveChangesAsync();
        }

        // Assert
        using (CatalogDbContext ctx = new CatalogDbContext(options, tenantMock.Object))
        {
            List<PackingUnit> remaining = await ctx.PackingUnits.ToListAsync();

            remaining.Should().HaveCount(1);
            remaining.Single().Id.Should().Be(survivingUnit.Id);

            bool productGone = await ctx.Products.AnyAsync(p => p.Id == doomedProduct.Id);
            productGone.Should().BeFalse();
        }
    }

    [Fact]
    public async Task ProductWithoutPackingUnits_CanBeDeleted()
    {
        // A product with no packing configured yet is a normal state during MVP data entry,
        // and deleting it must not depend on whether children exist.
        DbContextOptions<CatalogDbContext> options = BuildOptions();
        Mock<ITenantContext> tenantMock = TenantMock(Guid.NewGuid());

        Product product = BuildProduct();

        using (CatalogDbContext ctx = new CatalogDbContext(options, tenantMock.Object))
        {
            ctx.Products.Add(product);
            await ctx.SaveChangesAsync();
        }

        using (CatalogDbContext ctx = new CatalogDbContext(options, tenantMock.Object))
        {
            Product loaded = await ctx.Products
                .Include(p => p.PackingUnits)
                .SingleAsync(p => p.Id == product.Id);

            ctx.Products.Remove(loaded);
            await ctx.SaveChangesAsync();
        }

        using (CatalogDbContext ctx = new CatalogDbContext(options, tenantMock.Object))
        {
            bool exists = await ctx.Products.AnyAsync(p => p.Id == product.Id);
            exists.Should().BeFalse();
        }
    }
}
