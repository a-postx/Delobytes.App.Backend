using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Contracts.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure;

/// <summary>
/// Card-level isolation of the product import. A card that fails must leave neither its rows nor
/// its in-memory changes behind, so the next card of the batch starts from the committed state.
/// The in-memory provider has no savepoints, so these tests cover the change-tracker side; the
/// database side is the savepoint itself.
/// </summary>
public class ProductImportStoreRollbackTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _channelId = Guid.NewGuid();
    private readonly string _databaseName = Guid.NewGuid().ToString();

    [Fact]
    public async Task RollbackAsync_RevertsModifiedValuesToDatabaseValues()
    {
        await using CatalogDbContext context = BuildContext();
        Product product = await SeedProductAsync(context);
        ChannelProduct link = product.ChannelProducts.First();

        ProductImportStore store = new ProductImportStore(context);
        using IProductImportBatchTransaction batch = await store.BeginBatchAsync(CancellationToken.None);
        using IProductImportCardScope card = await batch.BeginCardAsync(CancellationToken.None);

        product.Name = "Changed by the failing card";
        link.Name = "Changed by the failing card";
        context.ChangeTracker.DetectChanges();

        await card.RollbackAsync(CancellationToken.None);

        product.Name.Should().Be("Seeded product");
        link.Name.Should().BeNull();
    }

    [Fact]
    public async Task RollbackAsync_KeepsABarcodeTheFailedCardRemoved()
    {
        await using CatalogDbContext context = BuildContext();
        Product product = await SeedProductAsync(context);
        ChannelProduct link = product.ChannelProducts.First();
        ChannelProductBarcode barcode = link.Barcodes.First();

        ProductImportStore store = new ProductImportStore(context);
        using IProductImportBatchTransaction batch = await store.BeginBatchAsync(CancellationToken.None);
        using IProductImportCardScope card = await batch.BeginCardAsync(CancellationToken.None);

        store.RemoveChannelProductBarcode(barcode);
        link.Barcodes.Remove(barcode);
        context.ChangeTracker.DetectChanges();

        await card.RollbackAsync(CancellationToken.None);

        link.Barcodes.Should().Contain(barcode);

        // Nothing may be deleted by the save that follows the failed card.
        await context.SaveChangesAsync();

        await using CatalogDbContext verifyContext = BuildContext();
        (await verifyContext.ChannelProductBarcodes.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RollbackAsync_DetachesABarcodeTheFailedCardAdded()
    {
        await using CatalogDbContext context = BuildContext();
        Product product = await SeedProductAsync(context);
        ChannelProduct link = product.ChannelProducts.First();

        ProductImportStore store = new ProductImportStore(context);
        using IProductImportBatchTransaction batch = await store.BeginBatchAsync(CancellationToken.None);
        using IProductImportCardScope card = await batch.BeginCardAsync(CancellationToken.None);

        ChannelProductBarcode added = new ChannelProductBarcode
        {
            ChannelProductId = link.Id,
            Value = "4600000000099",
            Type = "wildberries",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        store.AddChannelProductBarcode(added);
        link.Barcodes.Add(added);
        context.ChangeTracker.DetectChanges();

        context.Entry(added).State.Should().Be(EntityState.Added);

        await card.RollbackAsync(CancellationToken.None);

        context.Entry(added).State.Should().Be(EntityState.Detached);
        link.Barcodes.Should().NotContain(added);
    }

    [Fact]
    public async Task RollbackAsync_DetachesAPackingUnitTheFailedCardAddedToAnExistingProduct()
    {
        await using CatalogDbContext context = BuildContext();
        Product product = await SeedProductAsync(context);

        ProductImportStore store = new ProductImportStore(context);
        using IProductImportBatchTransaction batch = await store.BeginBatchAsync(CancellationToken.None);
        using IProductImportCardScope card = await batch.BeginCardAsync(CancellationToken.None);

        PackingUnit unit = new PackingUnit
        {
            ProductId = product.Id,
            ChannelId = _channelId,
            LengthCm = 30,
            WidthCm = 20,
            HeightCm = 10,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        product.PackingUnits.Add(unit);
        context.ChangeTracker.DetectChanges();

        context.Entry(unit).State.Should().Be(EntityState.Added);

        await card.RollbackAsync(CancellationToken.None);

        context.Entry(unit).State.Should().Be(EntityState.Detached);
        product.PackingUnits.Should().NotContain(unit);

        await context.SaveChangesAsync();

        await using CatalogDbContext verifyContext = BuildContext();
        (await verifyContext.Set<PackingUnit>().CountAsync()).Should().Be(0);
    }

    private async Task<Product> SeedProductAsync(CatalogDbContext context)
    {
        Product product = new Product
        {
            Id = Guid.NewGuid(),
            Code = "P-000001",
            Sku = "SKU-1",
            Name = "Seeded product",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };

        ChannelProduct link = new ChannelProduct
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ChannelId = _channelId,
            ExternalProductId = "123456789",
            ExternalSku = "ART-1",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };

        link.Barcodes.Add(new ChannelProductBarcode
        {
            Id = Guid.NewGuid(),
            ChannelProductId = link.Id,
            Value = "4600000000001",
            Type = "wildberries",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        });

        product.ChannelProducts.Add(link);
        await context.Products.AddAsync(product);
        await context.SaveChangesAsync();

        return product;
    }

    private CatalogDbContext BuildContext()
    {
        DbContextOptions<CatalogDbContext> options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(_databaseName)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        Mock<ITenantContext> tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(context => context.TenantId).Returns(_tenantId);

        return new CatalogDbContext(options, tenantContextMock.Object);
    }
}
