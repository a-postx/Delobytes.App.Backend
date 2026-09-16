using System;
using System.Security;
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
/// Same guard as <see cref="CatalogDbContextNoTenantGuardTests"/>, applied to PackingUnit.
/// Packing units will be created by a background import job before an operator ever edits
/// them, and a background job has no HttpContext, so TenantId resolves to null. Without the
/// guard the row would be written with TenantId = Guid.Empty and stay invisible to its tenant.
/// </summary>
public class PackingUnitNoTenantGuardTests
{
    private static DbContextOptions<CatalogDbContext> BuildOptions()
    {
        return new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("packing-unit-no-tenant-guard-" + Guid.NewGuid())
            .Options;
    }

    private static Mock<ITenantContext> TenantMock(Guid? tenantId)
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
            Name = "Товар",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static PackingUnit BuildPackingUnit(Guid productId)
    {
        return new PackingUnit
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            Name = "Коробка",
            LengthCm = 40m,
            WidthCm = 30m,
            HeightCm = 20m,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    [Fact]
    public async Task SaveChangesAsync_ContextWithoutTenant_AddingPackingUnit_Throws()
    {
        DbContextOptions<CatalogDbContext> options = BuildOptions();
        Mock<ITenantContext> noTenantMock = TenantMock(null); // e.g. a background job with no HttpContext

        Product product = BuildProduct();

        using CatalogDbContext ctx = new CatalogDbContext(options, noTenantMock.Object);
        ctx.PackingUnits.Add(BuildPackingUnit(product.Id));

        Func<Task> act = () => ctx.SaveChangesAsync();

        await act.Should().ThrowAsync<SecurityException>()
            .WithMessage("*PackingUnit*without a resolved TenantId*");
    }

    [Fact]
    public async Task SaveChangesAsync_ContextWithoutTenant_AddingPackingUnit_PersistsNothing()
    {
        DbContextOptions<CatalogDbContext> options = BuildOptions();
        Mock<ITenantContext> noTenantMock = TenantMock(null);
        PackingUnit orphanCandidate = BuildPackingUnit(Guid.NewGuid());

        using (CatalogDbContext ctx = new CatalogDbContext(options, noTenantMock.Object))
        {
            ctx.PackingUnits.Add(orphanCandidate);
            await Assert.ThrowsAsync<SecurityException>(() => ctx.SaveChangesAsync());
        }

        Mock<ITenantContext> tenantMock = TenantMock(Guid.NewGuid());
        using (CatalogDbContext ctx = new CatalogDbContext(options, tenantMock.Object))
        {
            bool exists = await ctx.PackingUnits
                .IgnoreQueryFilters()
                .AnyAsync(pu => pu.Id == orphanCandidate.Id);

            exists.Should().BeFalse("no PackingUnit with an unset TenantId should ever reach the store");
        }
    }

    [Fact]
    public async Task SaveChangesAsync_WithTenant_AddingPackingUnit_StillWorks()
    {
        DbContextOptions<CatalogDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenantMock = TenantMock(tenantId);

        Product product = BuildProduct();
        PackingUnit packingUnit = BuildPackingUnit(product.Id);

        using (CatalogDbContext ctx = new CatalogDbContext(options, tenantMock.Object))
        {
            ctx.PackingUnits.Add(packingUnit);
            await ctx.SaveChangesAsync();
        }

        using (CatalogDbContext ctx = new CatalogDbContext(options, tenantMock.Object))
        {
            bool exists = await ctx.PackingUnits.AnyAsync(pu => pu.Id == packingUnit.Id);
            exists.Should().BeTrue();
        }
    }
}
