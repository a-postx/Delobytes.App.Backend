using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Application.Packing;
using Delobytes.App.Backend.Catalog.Application.Products;
using Delobytes.App.Backend.Catalog.Application.Services;
using Delobytes.App.Backend.Catalog.Contracts.Events;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Catalog.Infrastructure.Messaging;
using Delobytes.App.Backend.Catalog.Infrastructure.Messaging.Consumers;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Contracts.Storage;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Tests.Application.Catalog.Numbering;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure;

/// <summary>
/// Integration tests for ProductBatchImporter.
/// Tests idempotent batch processing, product matching, barcode synchronization, and tenant isolation.
/// </summary>
public class ProductBatchImporterTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _channelId = Guid.NewGuid();
    private readonly Guid _connectionId = Guid.NewGuid();
    private readonly Guid _syncJobId = Guid.NewGuid();

    [Fact]
    public async Task ProcessAsync_CreatesNewProduct_WhenNoMatchExists()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        // Use fresh context for consumer to properly apply query filters
        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);

        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object);

        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Test Product",
            OfferId = "SKU-001",
            Barcodes = new List<string> { "1234567890123" },
            Description = "Test Description"
        };

        ProductBatchImportRequestedEvent message = new ProductBatchImportRequestedEvent
        {
            JobRunId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<ChannelProductCard> { card },
            IsLastBatch = false
        };

        // Act
        await importer.ImportAsync(message, CancellationToken.None);

        // Assert
        Product? product = await consumerContext.Products
            .IgnoreQueryFilters()
            .Include(p => p.Barcodes)
            .Include(p => p.ChannelProducts)
            .FirstOrDefaultAsync(p => p.Sku == "SKU-001");

        product.Should().NotBeNull();
        product!.Name.Should().Be("Test Product");
        product.Description.Should().Be("Test Description");
        product.Status.Should().Be(ProductStatus.Active);
        product.CreationSource.Should().Be(CreationSource.WildberriesImport);

        product.Barcodes.Should().HaveCount(1);
        product.Barcodes.First().Value.Should().Be("1234567890123");
        product.Barcodes.First().Type.Should().Be("wildberries");

        product.ChannelProducts.Should().HaveCount(1);
        ChannelProduct channelProduct = product.ChannelProducts.First();
        channelProduct.ExternalProductId.Should().Be("123456789");
        channelProduct.ExternalSku.Should().Be("SKU-001");
        channelProduct.IsActive.Should().BeTrue();
        channelProduct.ChannelId.Should().Be(_channelId);

        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ProductBatchImportCompletedEvent>(e =>
                    e.JobRunId == _syncJobId &&
                    e.RecordsProcessed == 1 &&
                    e.RecordsCreated == 1 &&
                    e.RecordsUpdated == 0 &&
                    e.RecordsSkipped == 0 &&
                    e.RecordsFailed == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_CreatesCanonicalAndChannelPackingUnit_WhenCardHasDimensions()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);

        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object);

        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Test Product",
            OfferId = "SKU-001",
            Barcodes = new List<string> { "1234567890123" },
            Description = "Test Description",
            LengthCm = 30,
            WidthCm = 20,
            HeightCm = 10,
            WeightKg = 0.5m
        };

        ProductBatchImportRequestedEvent message = new ProductBatchImportRequestedEvent
        {
            JobRunId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<ChannelProductCard> { card },
            IsLastBatch = false
        };

        // Act
        await importer.ImportAsync(message, CancellationToken.None);

        // Assert
        Product? product = await consumerContext.Products
            .IgnoreQueryFilters()
            .Include(p => p.PackingUnits)
            .FirstOrDefaultAsync(p => p.Sku == "SKU-001");

        product.Should().NotBeNull();
        product!.PackingUnits.Should().HaveCount(2, "the canonical unit and the one this channel reports");

        PackingUnit canonical = PackingUnitResolver.ResolveCanonical(product)!;
        canonical.ChannelId.Should().BeNull();
        canonical.LengthCm.Should().Be(30);
        canonical.WidthCm.Should().Be(20);
        canonical.HeightCm.Should().Be(10);
        canonical.WeightKg.Should().Be(0.5m);
        canonical.IsActive.Should().BeTrue();

        PackingUnit channelUnit = PackingUnitResolver.ResolveForChannel(product, _channelId)!;
        channelUnit.ChannelId.Should().Be(_channelId);
        channelUnit.LengthCm.Should().Be(30);
        channelUnit.WidthCm.Should().Be(20);
        channelUnit.HeightCm.Should().Be(10);
    }

    [Fact]
    public async Task ProcessAsync_DoesNotCreatePackingUnit_WhenCardHasNoDimensions()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);

        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object);

        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Test Product",
            OfferId = "SKU-001",
            Barcodes = new List<string> { "1234567890123" },
            Description = "Test Description"
        };

        ProductBatchImportRequestedEvent message = new ProductBatchImportRequestedEvent
        {
            JobRunId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<ChannelProductCard> { card },
            IsLastBatch = false
        };

        // Act
        await importer.ImportAsync(message, CancellationToken.None);

        // Assert
        Product? product = await consumerContext.Products
            .IgnoreQueryFilters()
            .Include(p => p.PackingUnits)
            .FirstOrDefaultAsync(p => p.Sku == "SKU-001");

        product.Should().NotBeNull();
        product!.PackingUnits.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_UpdatesOnlyChannelPackingUnit_WhenDimensionsChange()
    {
        // Arrange: the product already owns a canonical unit; the card reports its own packing,
        // which must be stored next to it rather than overwriting the catalogue's value.
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        Product existingProduct = new Product
        {
            Id = Guid.NewGuid(),
            Code = "P-000900",
            Sku = "SKU-001",
            Name = "Old Name",
            Description = "Old Description",
            Status = ProductStatus.Active,
            CreationSource = CreationSource.WildberriesImport,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        ChannelProduct existingChannelProduct = new ChannelProduct
        {
            Id = Guid.NewGuid(),
            ProductId = existingProduct.Id,
            ChannelId = _channelId,
            ExternalProductId = "123456789",
            ExternalSku = "SKU-001",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        PackingUnit existingPackingUnit = new PackingUnit
        {
            Id = Guid.NewGuid(),
            ProductId = existingProduct.Id,
            ChannelId = null,
            LengthCm = 10,
            WidthCm = 10,
            HeightCm = 10,
            WeightKg = 0.1m,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        existingProduct.ChannelProducts.Add(existingChannelProduct);
        existingProduct.PackingUnits.Add(existingPackingUnit);
        await setupContext.Products.AddAsync(existingProduct);
        await setupContext.SaveChangesAsync();

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);

        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object);

        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Updated Name",
            OfferId = "SKU-001",
            Barcodes = new List<string> { "1234567890123" },
            Description = "Updated Description",
            LengthCm = 40,
            WidthCm = 25,
            HeightCm = 15,
            WeightKg = 1.2m
        };

        ProductBatchImportRequestedEvent message = new ProductBatchImportRequestedEvent
        {
            JobRunId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<ChannelProductCard> { card },
            IsLastBatch = false
        };

        // Act
        await importer.ImportAsync(message, CancellationToken.None);

        // Assert
        Product? product = await consumerContext.Products
            .IgnoreQueryFilters()
            .Include(p => p.PackingUnits)
            .FirstOrDefaultAsync(p => p.Id == existingProduct.Id);

        product.Should().NotBeNull();
        product!.PackingUnits.Should().HaveCount(2, "the canonical unit stays and the channel one is added");

        PackingUnit canonical = PackingUnitResolver.ResolveCanonical(product)!;
        canonical.LengthCm.Should().Be(10, "the import never rewrites the catalogue's own dimensions");
        canonical.WidthCm.Should().Be(10);
        canonical.HeightCm.Should().Be(10);
        canonical.WeightKg.Should().Be(0.1m);

        PackingUnit channelUnit = PackingUnitResolver.ResolveForChannel(product, _channelId)!;
        channelUnit.LengthCm.Should().Be(40);
        channelUnit.WidthCm.Should().Be(25);
        channelUnit.HeightCm.Should().Be(15);
        channelUnit.WeightKg.Should().Be(1.2m);

        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ProductBatchImportCompletedEvent>(e =>
                    e.RecordsProcessed == 1 &&
                    e.RecordsCreated == 0 &&
                    e.RecordsUpdated == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_UpdatesOnlyChannelFields_WhenChannelProductExists()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        Product existingProduct = new Product
        {
            Id = Guid.NewGuid(),
            Code = "P-000900",
            Sku = "SKU-001",
            Name = "Old Name",
            Description = "Old Description",
            Status = ProductStatus.Active,
            CreationSource = CreationSource.WildberriesImport,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        ChannelProduct existingChannelProduct = new ChannelProduct
        {
            Id = Guid.NewGuid(),
            ProductId = existingProduct.Id,
            ChannelId = _channelId,
            ExternalProductId = "123456789",
            ExternalSku = "SKU-001",
            Name = "Old card name",
            Description = "Old card description",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        existingProduct.ChannelProducts.Add(existingChannelProduct);
        await setupContext.Products.AddAsync(existingProduct);
        await setupContext.SaveChangesAsync();

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);

        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object);

        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Updated Name",
            OfferId = "SKU-001",
            Barcodes = new List<string> { "1234567890123" },
            Description = "Updated Description"
        };

        ProductBatchImportRequestedEvent message = new ProductBatchImportRequestedEvent
        {
            JobRunId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<ChannelProductCard> { card },
            IsLastBatch = false
        };

        // Act
        await importer.ImportAsync(message, CancellationToken.None);

        // Assert
        Product? product = await consumerContext.Products
            .IgnoreQueryFilters()
            .Include(p => p.Barcodes)
            .Include(p => p.ChannelProducts)
            .FirstOrDefaultAsync(p => p.Id == existingProduct.Id);

        product.Should().NotBeNull();
        product!.Name.Should().Be("Old Name", "the catalogue name is written only at creation");
        product.Description.Should().Be("Old Description");

        ChannelProduct channelProduct = product.ChannelProducts.Should().ContainSingle().Subject;
        channelProduct.Name.Should().Be("Updated Name");
        channelProduct.Description.Should().Be("Updated Description");

        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ProductBatchImportCompletedEvent>(e =>
                    e.RecordsProcessed == 1 &&
                    e.RecordsCreated == 0 &&
                    e.RecordsUpdated == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_IsIdempotent_WhenBatchReprocessed()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);

        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object);

        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Test Product",
            OfferId = "SKU-001",
            Barcodes = new List<string> { "1234567890123" },
            Description = "Test Description"
        };

        ProductBatchImportRequestedEvent message = new ProductBatchImportRequestedEvent
        {
            JobRunId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<ChannelProductCard> { card },
            IsLastBatch = false
        };

        // Act - Process the same batch twice
        await importer.ImportAsync(message, CancellationToken.None);
        await importer.ImportAsync(message, CancellationToken.None);

        // Assert
        List<Product> products = await consumerContext.Products
            .IgnoreQueryFilters()
            .Include(p => p.Barcodes)
            .Include(p => p.ChannelProducts)
            .Where(p => p.Sku == "SKU-001")
            .ToListAsync();

        products.Should().HaveCount(1);
        products[0].ChannelProducts.Should().HaveCount(1);
        products[0].Barcodes.Where(b => b.Type == "wildberries").Should().HaveCount(1);

        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ProductBatchImportCompletedEvent>(e =>
                    e.RecordsProcessed == 1 &&
                    e.RecordsCreated == 1 &&
                    e.RecordsUpdated == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);

        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ProductBatchImportCompletedEvent>(e =>
                    e.RecordsProcessed == 1 &&
                    e.RecordsCreated == 0 &&
                    e.RecordsSkipped == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_CreatesSecondProductWithCodeAsSku_WhenVendorCodeMatchesAnUnlinkedProduct()
    {
        // Product.Sku is not a matching key: a card whose vendor code equals an existing product's
        // SKU is NOT linked to that product. It also cannot reuse the taken SKU, so it becomes a
        // new product whose SKU is its freshly issued code.
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        Product existingProduct = new Product
        {
            Id = Guid.NewGuid(),
            Code = "P-000900",
            Sku = "SKU-001",
            Name = "Existing Product",
            Status = ProductStatus.Active,
            CreationSource = CreationSource.Manual,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10)
        };

        await setupContext.Products.AddAsync(existingProduct);
        await setupContext.SaveChangesAsync();

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);

        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object);

        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Product from WB",
            OfferId = "SKU-001",
            Barcodes = new List<string> { "1234567890123" }
        };

        ProductBatchImportRequestedEvent message = new ProductBatchImportRequestedEvent
        {
            JobRunId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<ChannelProductCard> { card },
            IsLastBatch = false
        };

        // Act
        await importer.ImportAsync(message, CancellationToken.None);

        // Assert
        List<Product> products = await consumerContext.Products
            .IgnoreQueryFilters()
            .Include(p => p.ChannelProducts)
            .ToListAsync();

        products.Should().HaveCount(2);

        Product created = products.Should().ContainSingle(p => p.Id != existingProduct.Id).Subject;
        created.Sku.Should().Be(created.Code, "the vendor code was already another product's SKU");
        created.Code.Should().Be("P-000001");
        created.ChannelProducts.Should().ContainSingle();

        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ProductBatchImportCompletedEvent>(e =>
                    e.RecordsProcessed == 1 &&
                    e.RecordsCreated == 1 &&
                    e.RecordsFailed == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_LinksToExistingProduct_WhenBarcodeMatches()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        Product existingProduct = new Product
        {
            Id = Guid.NewGuid(),
            Code = "P-000901",
            Sku = "INTERNAL-SKU",
            Name = "Existing Product",
            Status = ProductStatus.Active,
            CreationSource = CreationSource.Manual,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10)
        };

        ProductBarcode existingBarcode = new ProductBarcode
        {
            Id = Guid.NewGuid(),
            ProductId = existingProduct.Id,
            Value = "9876543210987",
            Type = "EAN13",
            IsDefault = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10)
        };

        existingProduct.Barcodes.Add(existingBarcode);
        await setupContext.Products.AddAsync(existingProduct);
        await setupContext.SaveChangesAsync();

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);

        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object);

        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Product from WB",
            OfferId = "WB-SKU-001",
            Barcodes = new List<string> { "9876543210987", "1111111111111" }
        };

        ProductBatchImportRequestedEvent message = new ProductBatchImportRequestedEvent
        {
            JobRunId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<ChannelProductCard> { card },
            IsLastBatch = false
        };

        // Act
        await importer.ImportAsync(message, CancellationToken.None);

        // Assert
        List<Product> products = await consumerContext.Products
            .IgnoreQueryFilters()
            .Include(p => p.ChannelProducts)
            .Include(p => p.Barcodes)
            .Where(p => p.Sku == "INTERNAL-SKU")
            .ToListAsync();

        products.Should().HaveCount(1);
        products[0].Id.Should().Be(existingProduct.Id);
        products[0].ChannelProducts.Should().HaveCount(1);
        products[0].ChannelProducts.First().ExternalProductId.Should().Be("123456789");
        products[0].Barcodes.Where(b => b.Type == "wildberries").Should().HaveCount(2);
    }

    [Fact]
    public async Task ProcessAsync_CreatesProductWithCodeAsSku_WhenVendorCodeCollidesWithExistingInternalSku()
    {
        // A vendor code that already exists as an internal SKU must not become a duplicate: the
        // unique index on (TenantId, Sku) would reject it at SaveChanges and roll back the batch.
        // The card is created under its own code instead, and the batch still completes.
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        Product product1 = new Product
        {
            Id = Guid.NewGuid(),
            Code = "P-000900",
            Sku = "SKU-001",
            Name = "Product 1",
            Status = ProductStatus.Active,
            CreationSource = CreationSource.Manual,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10)
        };

        Product product2 = new Product
        {
            Id = Guid.NewGuid(),
            Code = "P-000901",
            Sku = "SKU-002",
            Name = "Product 2",
            Status = ProductStatus.Active,
            CreationSource = CreationSource.Manual,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-5)
        };

        await setupContext.Products.AddRangeAsync(product1, product2);
        await setupContext.SaveChangesAsync();

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);

        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object);

        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Ambiguous Product",
            OfferId = "SKU-001",
            Barcodes = new List<string>()
        };

        ProductBatchImportRequestedEvent message = new ProductBatchImportRequestedEvent
        {
            JobRunId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<ChannelProductCard> { card },
            IsLastBatch = false
        };

        // Act
        await importer.ImportAsync(message, CancellationToken.None);

        // Assert
        List<Product> allProducts = await consumerContext.Products
            .IgnoreQueryFilters()
            .Include(p => p.ChannelProducts)
            .ToListAsync();

        allProducts.Should().HaveCount(3, "the card becomes a product under its own code");

        Product imported = allProducts.Should()
            .ContainSingle(p => p.CreationSource == CreationSource.WildberriesImport).Subject;
        imported.Sku.Should().Be(imported.Code);
        imported.Sku.Should().NotBe("SKU-001");
        imported.ChannelProducts.Should().ContainSingle();

        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ProductBatchImportCompletedEvent>(e =>
                    e.RecordsProcessed == 1 &&
                    e.RecordsCreated == 1 &&
                    e.RecordsFailed == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_SynchronizesBarcodes_WithoutDuplication()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        Product existingProduct = new Product
        {
            Id = Guid.NewGuid(),
            Code = "P-000902",
            Sku = "SKU-001",
            Name = "Product",
            Status = ProductStatus.Active,
            CreationSource = CreationSource.WildberriesImport,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        ChannelProduct existingChannelProduct = new ChannelProduct
        {
            Id = Guid.NewGuid(),
            ProductId = existingProduct.Id,
            ChannelId = _channelId,
            ExternalProductId = "123456789",
            ExternalSku = "SKU-001",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        ProductBarcode existingBarcode = new ProductBarcode
        {
            Id = Guid.NewGuid(),
            ProductId = existingProduct.Id,
            Value = "1111111111111",
            Type = "WB",
            IsDefault = false,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        existingProduct.ChannelProducts.Add(existingChannelProduct);
        existingProduct.Barcodes.Add(existingBarcode);
        await setupContext.Products.AddAsync(existingProduct);
        await setupContext.SaveChangesAsync();

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);

        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object);

        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Product",
            OfferId = "SKU-001",
            Barcodes = new List<string> { "1111111111111", "2222222222222" }
        };

        ProductBatchImportRequestedEvent message = new ProductBatchImportRequestedEvent
        {
            JobRunId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<ChannelProductCard> { card },
            IsLastBatch = false
        };

        // Act
        await importer.ImportAsync(message, CancellationToken.None);

        // Assert
        Product? product = await consumerContext.Products
            .IgnoreQueryFilters()
            .Include(p => p.Barcodes)
            .FirstOrDefaultAsync(p => p.Id == existingProduct.Id);

        product.Should().NotBeNull();
        product!.Barcodes.Where(b => b.Type == "wildberries").Should().HaveCount(2);
        product.Barcodes.Should().Contain(b => b.Value == "1111111111111");
        product.Barcodes.Should().Contain(b => b.Value == "2222222222222");
    }

    [Fact]
    public async Task ProcessAsync_IsolatesTenants_AcrossDifferentContexts()
    {
        // Arrange
        Guid tenant1Id = Guid.NewGuid();
        Guid tenant2Id = Guid.NewGuid();
        string database1Name = Guid.NewGuid().ToString();
        string database2Name = Guid.NewGuid().ToString();

        CatalogDbContext setupContext1 = BuildCatalogDbContext(tenant1Id, database1Name);
        CatalogDbContext setupContext2 = BuildCatalogDbContext(tenant2Id, database2Name);

        await CreateChannelAsync(setupContext1, tenant1Id);
        await CreateChannelAsync(setupContext2, tenant2Id);

        CatalogDbContext consumerContext1 = BuildCatalogDbContext(tenant1Id, database1Name);
        CatalogDbContext consumerContext2 = BuildCatalogDbContext(tenant2Id, database2Name);

        Mock<IPublishEndpoint> publishEndpointMock1 = new Mock<IPublishEndpoint>();
        Mock<IPublishEndpoint> publishEndpointMock2 = new Mock<IPublishEndpoint>();

        ProductBatchImporter importerTenant1 = BuildImporter(consumerContext1, publishEndpointMock1.Object);
        ProductBatchImporter importerTenant2 = BuildImporter(consumerContext2, publishEndpointMock2.Object);

        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Shared Product",
            OfferId = "SKU-001",
            Barcodes = new List<string> { "1234567890123" }
        };

        ProductBatchImportRequestedEvent messageTenant1 = new ProductBatchImportRequestedEvent
        {
            JobRunId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<ChannelProductCard> { card },
            IsLastBatch = false
        };

        ProductBatchImportRequestedEvent messageTenant2 = new ProductBatchImportRequestedEvent
        {
            JobRunId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<ChannelProductCard> { card },
            IsLastBatch = false
        };

        // Act
        await importerTenant1.ImportAsync(messageTenant1, CancellationToken.None);
        await importerTenant2.ImportAsync(messageTenant2, CancellationToken.None);

        // Assert
        List<Product> productsTenant1 = await consumerContext1.Products.IgnoreQueryFilters().ToListAsync();
        List<Product> productsTenant2 = await consumerContext2.Products.IgnoreQueryFilters().ToListAsync();

        productsTenant1.Should().HaveCount(1);
        productsTenant2.Should().HaveCount(1);
        productsTenant1[0].Id.Should().NotBe(productsTenant2[0].Id);
    }

    [Fact]
    public async Task ProcessAsync_HandlesErrors_AndPublishesFailedCount()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);

        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object);

        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Product Without Channel",
            OfferId = "SKU-001",
            Barcodes = new List<string>()
        };

        ProductBatchImportRequestedEvent message = new ProductBatchImportRequestedEvent
        {
            JobRunId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<ChannelProductCard> { card },
            IsLastBatch = false
        };

        // Act
        await importer.ImportAsync(message, CancellationToken.None);

        // Assert
        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ProductBatchImportCompletedEvent>(e =>
                    e.RecordsFailed == 1 &&
                    e.ErrorMessage != null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_VendorCodeChanged_UpdatesChannelProductExternalSku()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        Product existingProduct = new Product
        {
            Id = Guid.NewGuid(),
            Code = "P-000903",
            Sku = "OLD-VENDOR-CODE",
            Name = "Test Product",
            Description = "Original Description",
            Status = ProductStatus.Active,
            CreationSource = CreationSource.WildberriesImport,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        ChannelProduct existingChannelProduct = new ChannelProduct
        {
            Id = Guid.NewGuid(),
            ProductId = existingProduct.Id,
            ChannelId = _channelId,
            ExternalProductId = "123456789", // nmID Р В Р’В Р РЋРІР‚СћР В Р Р‹Р В РЎвЂњР В Р Р‹Р Р†Р вЂљРЎв„ўР В Р’В Р вЂ™Р’В°Р В Р Р‹Р Р†Р вЂљР’ВР В Р Р‹Р Р†Р вЂљРЎв„ўР В Р Р‹Р В РЎвЂњР В Р Р‹Р В Р РЏ Р В Р Р‹Р Р†Р вЂљРЎв„ўР В Р’В Р вЂ™Р’ВµР В Р’В Р РЋР’В Р В Р’В Р вЂ™Р’В¶Р В Р’В Р вЂ™Р’Вµ
            ExternalSku = "OLD-VENDOR-CODE",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        existingProduct.ChannelProducts.Add(existingChannelProduct);
        await setupContext.Products.AddAsync(existingProduct);
        await setupContext.SaveChangesAsync();

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);

        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object);

        // Р В Р’В Р РЋРЎС™Р В Р’В Р РЋРІР‚СћР В Р’В Р В РІР‚В Р В Р’В Р вЂ™Р’В°Р В Р Р‹Р В Р РЏ Р В Р’В Р РЋРІР‚СњР В Р’В Р вЂ™Р’В°Р В Р Р‹Р В РІР‚С™Р В Р Р‹Р Р†Р вЂљРЎв„ўР В Р’В Р РЋРІР‚СћР В Р Р‹Р Р†Р вЂљР Р‹Р В Р’В Р РЋРІР‚СњР В Р’В Р вЂ™Р’В° Р В Р Р‹Р В РЎвЂњ Р В Р Р‹Р Р†Р вЂљРЎв„ўР В Р’В Р вЂ™Р’ВµР В Р’В Р РЋР’В Р В Р’В Р вЂ™Р’В¶Р В Р’В Р вЂ™Р’Вµ nmID, Р В Р’В Р В РІР‚В¦Р В Р’В Р РЋРІР‚Сћ Р В Р’В Р СћРІР‚ВР В Р Р‹Р В РІР‚С™Р В Р Р‹Р РЋРІР‚СљР В Р’В Р РЋРІР‚вЂњР В Р’В Р РЋРІР‚ВР В Р’В Р РЋР’В vendorCode
        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Updated Product Name",
            OfferId = "NEW-VENDOR-CODE", // Р В Р’В Р вЂ™Р’ВР В Р’В Р вЂ™Р’В·Р В Р’В Р РЋР’ВР В Р’В Р вЂ™Р’ВµР В Р’В Р В РІР‚В¦Р В Р’В Р РЋРІР‚ВР В Р’В Р вЂ™Р’В»Р В Р Р‹Р В РЎвЂњР В Р Р‹Р В Р РЏ!
            Barcodes = new List<string> { "1234567890123" },
            Description = "Updated Description"
        };

        ProductBatchImportRequestedEvent message = new ProductBatchImportRequestedEvent
        {
            JobRunId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<ChannelProductCard> { card },
            IsLastBatch = false
        };

        // Act
        await importer.ImportAsync(message, CancellationToken.None);

        // Assert
        Product? product = await consumerContext.Products
            .IgnoreQueryFilters()
            .Include(p => p.ChannelProducts)
            .FirstOrDefaultAsync(p => p.Id == existingProduct.Id);

        product.Should().NotBeNull();
        product!.Sku.Should().Be("OLD-VENDOR-CODE", "Product.Sku Р В Р’В Р В РІР‚В¦Р В Р’В Р вЂ™Р’Вµ Р В Р’В Р РЋРІР‚СћР В Р’В Р вЂ™Р’В±Р В Р’В Р В РІР‚В¦Р В Р’В Р РЋРІР‚СћР В Р’В Р В РІР‚В Р В Р’В Р вЂ™Р’В»Р В Р Р‹Р В Р РЏР В Р’В Р вЂ™Р’ВµР В Р Р‹Р Р†Р вЂљРЎв„ўР В Р Р‹Р В РЎвЂњР В Р Р‹Р В Р РЏ Р В Р’В Р РЋРІР‚вЂќР В Р Р‹Р В РІР‚С™Р В Р’В Р РЋРІР‚В Р В Р’В Р РЋРІР‚ВР В Р’В Р РЋР’ВР В Р’В Р РЋРІР‚вЂќР В Р’В Р РЋРІР‚СћР В Р Р‹Р В РІР‚С™Р В Р Р‹Р Р†Р вЂљРЎв„ўР В Р’В Р вЂ™Р’Вµ");
        product.Name.Should().Be("Test Product", "the catalogue name is written only at creation");
        product.Description.Should().Be("Original Description");

        ChannelProduct? channelProduct = product.ChannelProducts.FirstOrDefault();
        channelProduct.Should().NotBeNull();
        channelProduct!.ExternalProductId.Should().Be("123456789", "nmID Р В Р’В Р СћРІР‚ВР В Р’В Р РЋРІР‚СћР В Р’В Р вЂ™Р’В»Р В Р’В Р вЂ™Р’В¶Р В Р’В Р вЂ™Р’ВµР В Р’В Р В РІР‚В¦ Р В Р’В Р РЋРІР‚СћР В Р Р‹Р В РЎвЂњР В Р Р‹Р Р†Р вЂљРЎв„ўР В Р’В Р вЂ™Р’В°Р В Р Р‹Р Р†Р вЂљРЎв„ўР В Р Р‹Р В Р вЂ°Р В Р Р‹Р В РЎвЂњР В Р Р‹Р В Р РЏ Р В Р’В Р В РІР‚В¦Р В Р’В Р вЂ™Р’ВµР В Р’В Р РЋРІР‚ВР В Р’В Р вЂ™Р’В·Р В Р’В Р РЋР’ВР В Р’В Р вЂ™Р’ВµР В Р’В Р В РІР‚В¦Р В Р’В Р В РІР‚В¦Р В Р Р‹Р Р†Р вЂљРІвЂћвЂ“Р В Р’В Р РЋР’В");
        channelProduct.ExternalSku.Should().Be("NEW-VENDOR-CODE", "ExternalSku Р В Р’В Р СћРІР‚ВР В Р’В Р РЋРІР‚СћР В Р’В Р вЂ™Р’В»Р В Р’В Р вЂ™Р’В¶Р В Р’В Р вЂ™Р’ВµР В Р’В Р В РІР‚В¦ Р В Р’В Р РЋРІР‚СћР В Р’В Р вЂ™Р’В±Р В Р’В Р В РІР‚В¦Р В Р’В Р РЋРІР‚СћР В Р’В Р В РІР‚В Р В Р’В Р РЋРІР‚ВР В Р Р‹Р Р†Р вЂљРЎв„ўР В Р Р‹Р В Р вЂ°Р В Р Р‹Р В РЎвЂњР В Р Р‹Р В Р РЏ Р В Р’В Р В РІР‚В¦Р В Р’В Р вЂ™Р’В° Р В Р’В Р В РІР‚В¦Р В Р’В Р РЋРІР‚СћР В Р’В Р В РІР‚В Р В Р Р‹Р Р†Р вЂљРІвЂћвЂ“Р В Р’В Р Р†РІР‚С›РІР‚вЂњ vendorCode");

        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ProductBatchImportCompletedEvent>(e =>
                    e.RecordsProcessed == 1 &&
                    e.RecordsCreated == 0 &&
                    e.RecordsUpdated == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }


    // -----------------------------------------------------------------------
    // Photo import happens outside the database transaction and after products are durable
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_ImportsPhotosAfterProductsAreCommitted()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);

        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        Mock<IProductPhotoService> photoServiceMock = BuildPhotoServiceMock();

        ProductBatchImporter importer = BuildImporter(
            consumerContext,
            publishEndpointMock.Object,
            photoServiceMock);

        bool productWasVisibleWhenPhotosWereImported = false;

        photoServiceMock
            .Setup(s => s.ImportPhotosAsync(
                It.IsAny<Product>(),
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyList<MarketplacePhotoSource>>(),
                It.IsAny<CancellationToken>()))
            .Callback<Product, Guid, IReadOnlyList<MarketplacePhotoSource>, CancellationToken>((product, _, _, _) =>
            {
                // A separate context stands in for another connection: if the product is visible
                // here, the transaction that created it has already been committed.
                using CatalogDbContext observerContext = BuildCatalogDbContext(_tenantId, databaseName);
                productWasVisibleWhenPhotosWereImported = observerContext.Products
                    .IgnoreQueryFilters()
                    .Any(p => p.Id == product.Id);
            })
            .ReturnsAsync(new ProductPhotoImportResult { Imported = 1 });

        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Product With Photos",
            OfferId = "SKU-001",
            Barcodes = new List<string> { "1234567890123" },
            Photos = new List<ChannelProductPhoto>
            {
                new ChannelProductPhoto { Url = "https://example.test/t.webp", SizeVariant = "thumbnail", Width = 246, Height = 328, DisplayOrder = 1 }, new ChannelProductPhoto { Url = "https://example.test/l.webp", SizeVariant = "large", Width = 516, Height = 688, DisplayOrder = 1 },
            },
        };

        ProductBatchImportRequestedEvent message = new ProductBatchImportRequestedEvent
        {
            JobRunId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<ChannelProductCard> { card },
            IsLastBatch = true,
        };

        // Act
        await importer.ImportAsync(message, CancellationToken.None);

        // Assert
        photoServiceMock.Verify(
            s => s.ImportPhotosAsync(
                It.IsAny<Product>(),
                It.IsAny<Guid>(),
                It.Is<IReadOnlyList<MarketplacePhotoSource>>(sources => sources.Count == 2),
                It.IsAny<CancellationToken>()),
            Times.Once);

        productWasVisibleWhenPhotosWereImported.Should().BeTrue(
            "photos must be downloaded only after the product transaction has committed");

        // Counters reflect the database work only: photo import must not change them.
        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ProductBatchImportCompletedEvent>(e =>
                    e.RecordsProcessed == 1 &&
                    e.RecordsCreated == 1 &&
                    e.RecordsFailed == 0 &&
                    e.IsLastBatch),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_PhotoImportFailure_DoesNotFailTheBatch()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);

        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        Mock<IProductPhotoService> photoServiceMock = BuildPhotoServiceMock();

        ProductBatchImporter importer = BuildImporter(
            consumerContext,
            publishEndpointMock.Object,
            photoServiceMock);

        photoServiceMock
            .Setup(s => s.ImportPhotosAsync(
                It.IsAny<Product>(),
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyList<MarketplacePhotoSource>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("storage unavailable"));

        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Product With Broken Photos",
            OfferId = "SKU-001",
            Barcodes = new List<string> { "1234567890123" },
            Photos = new List<ChannelProductPhoto>
            {
                new ChannelProductPhoto { Url = "https://example.test/t.webp", SizeVariant = "thumbnail", Width = 246, Height = 328, DisplayOrder = 1 },
            },
        };

        ProductBatchImportRequestedEvent message = new ProductBatchImportRequestedEvent
        {
            JobRunId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<ChannelProductCard> { card },
            IsLastBatch = false,
        };

        // Act
        await importer.ImportAsync(message, CancellationToken.None);

        // Assert
        Product? product = await consumerContext.Products
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Sku == "SKU-001");

        product.Should().NotBeNull("the product survives a failed photo download");

        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ProductBatchImportCompletedEvent>(e =>
                    e.RecordsProcessed == 1 &&
                    e.RecordsCreated == 1 &&
                    e.RecordsFailed == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // Photos of a card are imported after the database transaction has committed,
    // and they do not distort the batch counters.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_ImportsPhotosAfterCommit_AndKeepsCountersUnchanged()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);

        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        Mock<IProductPhotoService> photoServiceMock = BuildPhotoServiceMock();

        // The commit must have happened before any photo is downloaded.
        bool committedBeforePhotoImport = false;
        photoServiceMock
            .Setup(s => s.ImportPhotosAsync(
                It.IsAny<Product>(),
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyList<MarketplacePhotoSource>>(),
                It.IsAny<CancellationToken>()))
            .Callback<Product, Guid, IReadOnlyList<MarketplacePhotoSource>, CancellationToken>((_, _, _, _) =>
            {
                // A committed product is visible to a second context; an uncommitted one is not.
                using CatalogDbContext probeContext = BuildCatalogDbContext(_tenantId, databaseName);

                committedBeforePhotoImport = probeContext.Products
                    .IgnoreQueryFilters()
                    .Any(p => p.Sku == "SKU-001");
            })
            .ReturnsAsync(new ProductPhotoImportResult { Imported = 2 });

        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object, photoServiceMock);

        ChannelProductCard card = BuildCardWithPhotos();

        ProductBatchImportRequestedEvent message = new ProductBatchImportRequestedEvent
        {
            JobRunId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<ChannelProductCard> { card },
            IsLastBatch = true
        };

        // Act
        await importer.ImportAsync(message, CancellationToken.None);

        // Assert
        committedBeforePhotoImport.Should().BeTrue(
            "Р В Р Р‹Р Р†Р вЂљРЎвЂєР В Р’В Р РЋРІР‚СћР В Р Р‹Р Р†Р вЂљРЎв„ўР В Р’В Р РЋРІР‚Сћ Р В Р’В Р вЂ™Р’В·Р В Р’В Р вЂ™Р’В°Р В Р’В Р РЋРІР‚вЂњР В Р Р‹Р В РІР‚С™Р В Р Р‹Р РЋРІР‚СљР В Р’В Р вЂ™Р’В¶Р В Р’В Р вЂ™Р’В°Р В Р Р‹Р В РІР‚в„–Р В Р Р‹Р Р†Р вЂљРЎв„ўР В Р Р‹Р В РЎвЂњР В Р Р‹Р В Р РЏ Р В Р’В Р РЋРІР‚вЂќР В Р’В Р РЋРІР‚СћР В Р Р‹Р В РЎвЂњР В Р’В Р вЂ™Р’В»Р В Р’В Р вЂ™Р’Вµ Р В Р’В Р РЋРІР‚СњР В Р’В Р РЋРІР‚СћР В Р’В Р РЋР’ВР В Р’В Р РЋР’ВР В Р’В Р РЋРІР‚ВР В Р Р‹Р Р†Р вЂљРЎв„ўР В Р’В Р вЂ™Р’В° Р В Р Р‹Р Р†Р вЂљРЎв„ўР В Р Р‹Р В РІР‚С™Р В Р’В Р вЂ™Р’В°Р В Р’В Р В РІР‚В¦Р В Р’В Р вЂ™Р’В·Р В Р’В Р вЂ™Р’В°Р В Р’В Р РЋРІР‚СњР В Р Р‹Р Р†Р вЂљР’В Р В Р’В Р РЋРІР‚ВР В Р’В Р РЋРІР‚В");

        photoServiceMock.Verify(
            s => s.ImportPhotosAsync(
                It.IsAny<Product>(),
                It.IsAny<Guid>(),
                It.Is<IReadOnlyList<MarketplacePhotoSource>>(sources => sources.Count == 2),
                It.IsAny<CancellationToken>()),
            Times.Once);

        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ProductBatchImportCompletedEvent>(e =>
                    e.RecordsProcessed == 1 &&
                    e.RecordsCreated == 1 &&
                    e.RecordsUpdated == 0 &&
                    e.RecordsSkipped == 0 &&
                    e.RecordsFailed == 0 &&
                    e.IsLastBatch),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_QueuesEmptyPhotoSet_WhenCardHasNoPhotos()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);

        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        Mock<IProductPhotoService> photoServiceMock = BuildPhotoServiceMock();

        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object, photoServiceMock);

        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Test Product",
            OfferId = "SKU-001",
            Barcodes = new List<string> { "1234567890123" },
            Description = "Test Description"
        };

        ProductBatchImportRequestedEvent message = new ProductBatchImportRequestedEvent
        {
            JobRunId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<ChannelProductCard> { card },
            IsLastBatch = false
        };

        // Act
        await importer.ImportAsync(message, CancellationToken.None);

        // Assert
        // A card that stopped reporting photos is the only signal that the channel's photos were
        // removed, so the import must run with an empty declaration instead of being skipped.
        photoServiceMock.Verify(
            s => s.ImportPhotosAsync(
                It.IsAny<Product>(),
                _channelId,
                It.Is<IReadOnlyList<MarketplacePhotoSource>>(sources => sources.Count == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_CardWithoutPhotos_RemovesOnlyThatChannelsPhotos()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        Guid otherChannelId = Guid.NewGuid();

        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        Product product = new Product
        {
            Id = Guid.NewGuid(),
            Code = "P-000904",
            Sku = "SKU-001",
            Name = "Test Product",
            Status = ProductStatus.Active,
            CreationSource = CreationSource.WildberriesImport,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };

        product.ChannelProducts.Add(new ChannelProduct
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ChannelId = _channelId,
            ExternalProductId = "123456789",
            ExternalSku = "SKU-001",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        });

        product.Photos.Add(BuildStoredPhoto(product.Id, _channelId, "1_1_thumbnail", "product-photos/importing.webp"));
        product.Photos.Add(BuildStoredPhoto(product.Id, otherChannelId, "1_1_thumbnail", "product-photos/other.webp"));

        await setupContext.Products.AddAsync(product);
        await setupContext.SaveChangesAsync();

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);

        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        Mock<IObjectStorage> storageMock = new Mock<IObjectStorage>();
        ProductPhotoService photoService = BuildRealPhotoService(storageMock);

        ProductBatchImporter importer = new ProductBatchImporter(
            new ProductImportStore(consumerContext),
            photoService,
            new MassTransitEventPublisher(publishEndpointMock.Object),
            new FakeNumberingSchemeProvider(),
            new FakeNumberAllocator(),
            Mock.Of<ILogger<ProductBatchImporter>>(),
            TimeProvider.System);

        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Test Product",
            OfferId = "SKU-001",
            Barcodes = new List<string> { "1234567890123" },
        };

        ProductBatchImportRequestedEvent message = new ProductBatchImportRequestedEvent
        {
            JobRunId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<ChannelProductCard> { card },
            IsLastBatch = true,
        };

        // Act
        await importer.ImportAsync(message, CancellationToken.None);

        // Assert
        CatalogDbContext assertContext = BuildCatalogDbContext(_tenantId, databaseName);

        List<ProductPhoto> photos = await assertContext.ProductPhotos
            .IgnoreQueryFilters()
            .Where(p => p.ProductId == product.Id)
            .ToListAsync();

        ProductPhoto importedChannelPhoto = photos.Should().ContainSingle(p => p.ChannelId == _channelId).Subject;
        importedChannelPhoto.Status.Should().Be(ProductPhotoStatus.Removed);
        importedChannelPhoto.RemovedAt.Should().NotBeNull();

        ProductPhoto otherChannelPhoto = photos.Should().ContainSingle(p => p.ChannelId == otherChannelId).Subject;
        otherChannelPhoto.Status.Should().Be(ProductPhotoStatus.Uploaded);

        // Reconciliation only marks rows; the blob is collected by the sweeper.
        storageMock.Verify(
            s => s.DeleteManyAsync(
                It.IsAny<StorageArea>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_PublishesPrimaryPhotoOfTheImportingChannel()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        Guid otherChannelId = Guid.NewGuid();

        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        Product product = new Product
        {
            Id = Guid.NewGuid(),
            Code = "P-000904",
            Sku = "SKU-001",
            Name = "Test Product",
            Status = ProductStatus.Active,
            CreationSource = CreationSource.WildberriesImport,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };

        product.ChannelProducts.Add(new ChannelProduct
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ChannelId = _channelId,
            ExternalProductId = "123456789",
            ExternalSku = "SKU-001",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        });

        product.Photos.Add(BuildStoredPhoto(product.Id, _channelId, "1_1_thumbnail", "product-photos/importing-channel.webp"));
        product.Photos.Add(BuildStoredPhoto(product.Id, otherChannelId, "1_1_thumbnail", "product-photos/other-channel.webp"));

        await setupContext.Products.AddAsync(product);
        await setupContext.SaveChangesAsync();

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);

        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        Mock<IProductPhotoService> photoServiceMock = BuildPhotoServiceMock();

        photoServiceMock
            .Setup(s => s.GetPublicUrl(It.IsAny<ProductPhoto>()))
            .Returns<ProductPhoto>(p => "https://cdn.example.com/" + p.StorageKey);

        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object, photoServiceMock);

        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Test Product",
            OfferId = "SKU-001",
            Barcodes = new List<string> { "1234567890123" },
            Photos = new List<ChannelProductPhoto>
            {
                new ChannelProductPhoto { Url = "https://example.test/t.webp", SizeVariant = "thumbnail", Width = 246, Height = 328, DisplayOrder = 1 },
            },
        };

        ProductBatchImportRequestedEvent message = new ProductBatchImportRequestedEvent
        {
            JobRunId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<ChannelProductCard> { card },
            IsLastBatch = true,
        };

        // Act
        await importer.ImportAsync(message, CancellationToken.None);

        // Assert
        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ChannelProductLinkedEvent>(e =>
                    e.ChannelId == _channelId
                    && e.PhotoUrl == "https://cdn.example.com/product-photos/importing-channel.webp"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // Matching: link -> barcode -> ExternalSku; product fields are written once,
    // channel fields refresh on every import.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_MatchesByBarcode_LinksTheUnlinkedProductAndKeepsItsName()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        Product existingProduct = new Product
        {
            Id = Guid.NewGuid(),
            Code = "P-000910",
            Sku = "INTERNAL-SKU",
            Name = "Seed Name",
            Status = ProductStatus.Active,
            CreationSource = CreationSource.Manual,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
        };

        existingProduct.Barcodes.Add(new ProductBarcode
        {
            Id = Guid.NewGuid(),
            ProductId = existingProduct.Id,
            Value = "1111111111111",
            Type = ChannelCodes.Wildberries,
            IsDefault = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
        });

        await setupContext.Products.AddAsync(existingProduct);
        await setupContext.SaveChangesAsync();

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);
        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object);

        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "777000111",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Card Name",
            OfferId = "CARD-ART",
            Barcodes = new List<string> { "1111111111111" },
        };

        // Act
        await importer.ImportAsync(BuildMessage(card), CancellationToken.None);

        // Assert
        List<Product> products = await consumerContext.Products
            .IgnoreQueryFilters()
            .Include(p => p.ChannelProducts)
            .ToListAsync();

        products.Should().ContainSingle();
        products[0].Name.Should().Be("Seed Name", "the catalogue name is not overwritten by a link");
        products[0].ChannelProducts.Should().ContainSingle()
            .Which.ExternalProductId.Should().Be("777000111");

        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ProductBatchImportCompletedEvent>(e => e.RecordsCreated == 0 && e.RecordsUpdated == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_CardWithoutBarcodeAndOfferId_IsSkippedAndInsertsNothing()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);
        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object);

        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "555000222",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Channel draft",
            OfferId = null,
            Barcodes = new List<string>(),
        };

        // Act
        await importer.ImportAsync(BuildMessage(card), CancellationToken.None);

        // Assert
        (await consumerContext.Products.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await consumerContext.ChannelProducts.IgnoreQueryFilters().CountAsync()).Should().Be(0);

        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ProductBatchImportCompletedEvent>(e =>
                    e.RecordsProcessed == 1 && e.RecordsCreated == 0 && e.RecordsSkipped == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_MatchesByExternalSkuOnAnotherChannel_LinksInsteadOfCreating()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        Guid otherChannelId = Guid.NewGuid();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);
        await setupContext.Channels.AddAsync(BuildChannel(otherChannelId, "Ozon"));
        await setupContext.SaveChangesAsync();

        Product existingProduct = new Product
        {
            Id = Guid.NewGuid(),
            Code = "P-000911",
            Sku = "OTHER-SKU",
            Name = "Product known from another channel",
            Status = ProductStatus.Active,
            CreationSource = CreationSource.Manual,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-5),
        };

        existingProduct.ChannelProducts.Add(new ChannelProduct
        {
            Id = Guid.NewGuid(),
            ProductId = existingProduct.Id,
            ChannelId = otherChannelId,
            ExternalProductId = "999000111",
            ExternalSku = "SHARED-ART",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-5),
        });

        await setupContext.Products.AddAsync(existingProduct);
        await setupContext.SaveChangesAsync();

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);
        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object);

        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "777000333",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Card with the same seller article",
            OfferId = "SHARED-ART",
            Barcodes = new List<string>(),
        };

        // Act
        await importer.ImportAsync(BuildMessage(card), CancellationToken.None);

        // Assert
        List<Product> products = await consumerContext.Products
            .IgnoreQueryFilters()
            .Include(p => p.ChannelProducts)
            .ToListAsync();

        products.Should().ContainSingle("the seller article identifies the product across channels");
        products[0].ChannelProducts.Should().HaveCount(2);
        products[0].ChannelProducts.Should().ContainSingle(cp => cp.ChannelId == _channelId);

        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ProductBatchImportCompletedEvent>(e => e.RecordsCreated == 0 && e.RecordsUpdated == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ExternalSkuMatchesTwoProducts_IsSkipped()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        Guid firstChannelId = Guid.NewGuid();
        Guid secondChannelId = Guid.NewGuid();

        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);
        await setupContext.Channels.AddAsync(BuildChannel(firstChannelId, "Ozon"));
        await setupContext.Channels.AddAsync(BuildChannel(secondChannelId, "Yandex Kit"));

        Product firstProduct = BuildSeededProduct("P-000912", "SKU-A", "First product");
        firstProduct.ChannelProducts.Add(BuildSeededLink(firstProduct.Id, firstChannelId, "100", "DUP-ART"));

        Product secondProduct = BuildSeededProduct("P-000913", "SKU-B", "Second product");
        secondProduct.ChannelProducts.Add(BuildSeededLink(secondProduct.Id, secondChannelId, "200", "DUP-ART"));

        await setupContext.Products.AddRangeAsync(firstProduct, secondProduct);
        await setupContext.SaveChangesAsync();

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);
        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object);

        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "777000444",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Ambiguous card",
            OfferId = "DUP-ART",
            Barcodes = new List<string>(),
        };

        // Act
        await importer.ImportAsync(BuildMessage(card), CancellationToken.None);

        // Assert
        (await consumerContext.Products.IgnoreQueryFilters().CountAsync()).Should().Be(2);
        (await consumerContext.ChannelProducts.IgnoreQueryFilters().CountAsync())
            .Should().Be(2, "an ambiguous match must not invent a third link");

        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ProductBatchImportCompletedEvent>(e =>
                    e.RecordsCreated == 0 && e.RecordsSkipped == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_NewCard_CopiesNameDescriptionSkuAndPackingUnit()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);
        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object);

        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Card name",
            Description = "Card description",
            OfferId = "SKU-COPY",
            Barcodes = new List<string> { "4600000000001" },
            LengthCm = 10,
            WidthCm = 20,
            HeightCm = 30,
            WeightKg = 1.5m,
        };

        // Act
        await importer.ImportAsync(BuildMessage(card), CancellationToken.None);

        // Assert
        Product product = (await consumerContext.Products
            .IgnoreQueryFilters()
            .Include(p => p.PackingUnits)
            .Include(p => p.ChannelProducts)
            .ToListAsync())
            .Should().ContainSingle().Subject;

        product.Name.Should().Be("Card name");
        product.Description.Should().Be("Card description");
        product.Sku.Should().Be("SKU-COPY");
        product.Code.Should().MatchRegex("^P-\\d{6}$");

        PackingUnit canonical = PackingUnitResolver.ResolveCanonical(product)!;
        canonical.LengthCm.Should().Be(10);
        canonical.WidthCm.Should().Be(20);
        canonical.HeightCm.Should().Be(30);
        canonical.WeightKg.Should().Be(1.5m);

        PackingUnitResolver.ResolveForChannel(product, _channelId).Should().NotBeNull();

        ChannelProduct channelProduct = product.ChannelProducts.Should().ContainSingle().Subject;
        channelProduct.Name.Should().Be("Card name");
        channelProduct.Description.Should().Be("Card description");
        channelProduct.ExternalSku.Should().Be("SKU-COPY");
    }

    [Fact]
    public async Task ProcessAsync_SameBarcodeInTwoChannels_YieldsOneProductAndTwoLinks()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        Guid secondChannelId = Guid.NewGuid();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);
        await setupContext.Channels.AddAsync(BuildChannel(secondChannelId, "Ozon"));
        await setupContext.SaveChangesAsync();

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);
        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object);

        ChannelProductCard firstCard = new ChannelProductCard
        {
            ExternalProductId = "111000111",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Card on the first channel",
            OfferId = "ART-WB",
            Barcodes = new List<string> { "4600000000009" },
        };

        ChannelProductCard secondCard = new ChannelProductCard
        {
            ExternalProductId = "222000222",
            ChannelCode = "ozon",
            BarcodeType = "ozon",
            Name = "Card on the second channel",
            OfferId = "ART-OZON",
            Barcodes = new List<string> { "4600000000009" },
        };

        // Act
        await importer.ImportAsync(BuildMessage(firstCard), CancellationToken.None);
        await importer.ImportAsync(BuildMessage(secondCard, secondChannelId), CancellationToken.None);

        // Assert
        List<Product> products = await consumerContext.Products
            .IgnoreQueryFilters()
            .Include(p => p.ChannelProducts)
            .ToListAsync();

        products.Should().ContainSingle("the shared barcode identifies one product");
        products[0].ChannelProducts.Should().HaveCount(2);
        products[0].ChannelProducts.Select(cp => cp.ChannelId)
            .Should().BeEquivalentTo(new[] { _channelId, secondChannelId });
    }

    [Fact]
    public async Task ProcessAsync_ChannelExternalSku_IsPopulatedOnCreateAndUpdatedOnChange()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);
        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object);

        ChannelProductCard creatingCard = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Product",
            OfferId = "ART-1",
            Barcodes = new List<string> { "4600000000002" },
        };

        // Act
        await importer.ImportAsync(BuildMessage(creatingCard), CancellationToken.None);

        // Assert: created with the seller article the card carries.
        ChannelProduct created = (await consumerContext.ChannelProducts
            .IgnoreQueryFilters()
            .ToListAsync()).Should().ContainSingle().Subject;
        created.ExternalSku.Should().Be("ART-1");

        // Act: the channel renames the article of the same card.
        ChannelProductCard updatingCard = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Product",
            OfferId = "ART-2",
            Barcodes = new List<string> { "4600000000002" },
        };

        await importer.ImportAsync(BuildMessage(updatingCard), CancellationToken.None);

        // Assert
        CatalogDbContext assertContext = BuildCatalogDbContext(_tenantId, databaseName);
        ChannelProduct updated = (await assertContext.ChannelProducts
            .IgnoreQueryFilters()
            .ToListAsync()).Should().ContainSingle().Subject;
        updated.ExternalSku.Should().Be("ART-2");

        Product product = (await assertContext.Products.IgnoreQueryFilters().ToListAsync())
            .Should().ContainSingle().Subject;
        product.Sku.Should().Be("ART-1", "the internal SKU is not rewritten by a channel change");
    }

    [Fact]
    public async Task ProcessAsync_ExternalSkuMatchAlreadyLinkedToSameChannel_IsSkipped()
    {
        // Arrange: the product already has a card on this channel, under another external id.
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        Product existingProduct = BuildSeededProduct("P-000914", "INTERNAL", "Already linked");
        ChannelProduct existingLink = BuildSeededLink(existingProduct.Id, _channelId, "old-external-id", "ART-X");
        existingProduct.ChannelProducts.Add(existingLink);

        await setupContext.Products.AddAsync(existingProduct);
        await setupContext.SaveChangesAsync();

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);
        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object);

        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "new-external-id",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Second card of the same product",
            OfferId = "ART-X",
            Barcodes = new List<string>(),
        };

        // Act
        await importer.ImportAsync(BuildMessage(card), CancellationToken.None);

        // Assert
        ChannelProduct link = (await consumerContext.ChannelProducts
            .IgnoreQueryFilters()
            .ToListAsync()).Should().ContainSingle().Subject;
        link.ExternalProductId.Should().Be("old-external-id");

        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ProductBatchImportCompletedEvent>(e =>
                    e.RecordsCreated == 0 && e.RecordsSkipped == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_AmbiguousBarcodeAndNoArticleMatch_CreatesNothing()
    {
        // Arrange: each barcode belongs to a different product, so the card points at two.
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        Product firstProduct = BuildSeededProduct("P-000915", "SKU-A", "First");
        firstProduct.Barcodes.Add(BuildSeededBarcode(firstProduct.Id, "1111111111111"));

        Product secondProduct = BuildSeededProduct("P-000916", "SKU-B", "Second");
        secondProduct.Barcodes.Add(BuildSeededBarcode(secondProduct.Id, "2222222222222"));

        await setupContext.Products.AddRangeAsync(firstProduct, secondProduct);
        await setupContext.SaveChangesAsync();

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);
        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object);

        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "777000555",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Card pointing at two products",
            OfferId = "NO-MATCH",
            Barcodes = new List<string> { "1111111111111", "2222222222222" },
        };

        // Act
        await importer.ImportAsync(BuildMessage(card), CancellationToken.None);

        // Assert
        (await consumerContext.Products.IgnoreQueryFilters().CountAsync()).Should().Be(2);
        (await consumerContext.ChannelProducts.IgnoreQueryFilters().CountAsync()).Should().Be(0);

        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ProductBatchImportCompletedEvent>(e =>
                    e.RecordsCreated == 0 && e.RecordsSkipped == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ZeroDimensions_CreateNoUnits_ThenRealDimensionsCreateBoth()
    {
        // Arrange: a card that reports zeros reports nothing usable at all.
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);
        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object);

        ChannelProductCard cardWithoutDimensions = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Product without dimensions",
            OfferId = "SKU-DIM",
            Barcodes = new List<string> { "4600000000003" },
            LengthCm = 0,
            WidthCm = 0,
            HeightCm = 0,
        };

        // Act
        await importer.ImportAsync(BuildMessage(cardWithoutDimensions), CancellationToken.None);

        // Assert
        Product productWithoutUnits = (await consumerContext.Products
            .IgnoreQueryFilters()
            .Include(p => p.PackingUnits)
            .ToListAsync()).Should().ContainSingle().Subject;
        productWithoutUnits.PackingUnits.Should().BeEmpty();

        // Act: the same card starts reporting real dimensions.
        ChannelProductCard cardWithDimensions = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Product without dimensions",
            OfferId = "SKU-DIM",
            Barcodes = new List<string> { "4600000000003" },
            LengthCm = 10,
            WidthCm = 20,
            HeightCm = 30,
            WeightKg = 2m,
        };

        await importer.ImportAsync(BuildMessage(cardWithDimensions), CancellationToken.None);

        // Assert
        CatalogDbContext assertContext = BuildCatalogDbContext(_tenantId, databaseName);
        Product product = (await assertContext.Products
            .IgnoreQueryFilters()
            .Include(p => p.PackingUnits)
            .ToListAsync()).Should().ContainSingle().Subject;

        product.PackingUnits.Should().HaveCount(2);
        PackingUnitResolver.ResolveCanonical(product)!.LengthCm.Should().Be(10);
        PackingUnitResolver.ResolveForChannel(product, _channelId)!.LengthCm.Should().Be(10);
    }

    [Fact]
    public async Task ProcessAsync_CanonicalUnitWrittenOnce_LaterImportUpdatesOnlyTheChannelRow()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);
        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object);

        ChannelProductCard firstCard = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Product",
            OfferId = "SKU-ONCE",
            Barcodes = new List<string> { "4600000000004" },
            LengthCm = 10,
            WidthCm = 10,
            HeightCm = 10,
            WeightKg = 0.5m,
        };

        // Act
        await importer.ImportAsync(BuildMessage(firstCard), CancellationToken.None);

        ChannelProductCard secondCard = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Product",
            OfferId = "SKU-ONCE",
            Barcodes = new List<string> { "4600000000004" },
            LengthCm = 40,
            WidthCm = 50,
            HeightCm = 60,
            WeightKg = 2m,
        };

        await importer.ImportAsync(BuildMessage(secondCard), CancellationToken.None);

        // Assert
        CatalogDbContext assertContext = BuildCatalogDbContext(_tenantId, databaseName);
        Product product = (await assertContext.Products
            .IgnoreQueryFilters()
            .Include(p => p.PackingUnits)
            .ToListAsync()).Should().ContainSingle().Subject;

        PackingUnit canonical = PackingUnitResolver.ResolveCanonical(product)!;
        canonical.LengthCm.Should().Be(10);
        canonical.WidthCm.Should().Be(10);
        canonical.HeightCm.Should().Be(10);
        canonical.WeightKg.Should().Be(0.5m);

        PackingUnit channelUnit = PackingUnitResolver.ResolveForChannel(product, _channelId)!;
        channelUnit.LengthCm.Should().Be(40);
        channelUnit.WidthCm.Should().Be(50);
        channelUnit.HeightCm.Should().Be(60);
        channelUnit.WeightKg.Should().Be(2m);
    }

    [Fact]
    public async Task ProcessAsync_IncompleteDimensions_LeaveTheChannelRowUnchanged()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);
        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object);

        ChannelProductCard completeCard = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Product",
            OfferId = "SKU-PARTIAL",
            Barcodes = new List<string> { "4600000000005" },
            LengthCm = 10,
            WidthCm = 20,
            HeightCm = 30,
            WeightKg = 1m,
        };

        // Act
        await importer.ImportAsync(BuildMessage(completeCard), CancellationToken.None);

        ChannelProductCard incompleteCard = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Product",
            OfferId = "SKU-PARTIAL",
            Barcodes = new List<string> { "4600000000005" },
            LengthCm = 50,
            WidthCm = 60,
            HeightCm = null,
            WeightKg = 9m,
        };

        await importer.ImportAsync(BuildMessage(incompleteCard), CancellationToken.None);

        // Assert: a partial report must not be zero-filled over the earlier complete one.
        CatalogDbContext assertContext = BuildCatalogDbContext(_tenantId, databaseName);
        Product product = (await assertContext.Products
            .IgnoreQueryFilters()
            .Include(p => p.PackingUnits)
            .ToListAsync()).Should().ContainSingle().Subject;

        PackingUnit channelUnit = PackingUnitResolver.ResolveForChannel(product, _channelId)!;
        channelUnit.LengthCm.Should().Be(10);
        channelUnit.WidthCm.Should().Be(20);
        channelUnit.HeightCm.Should().Be(30);
        channelUnit.WeightKg.Should().Be(1m);
    }

    private static ProductPhoto BuildStoredPhoto(
        Guid productId,
        Guid channelId,
        string externalId,
        string storageKey)
    {
        return new ProductPhoto
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            ChannelId = channelId,
            ExternalId = externalId,
            DisplayOrder = 1,
            SizeVariant = "thumbnail",
            StorageKey = storageKey,
            OriginalFileName = "photo.webp",
            ContentType = "image/webp",
            SizeBytes = 64,
            Source = "Wildberries",
            Status = ProductPhotoStatus.Uploaded,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
    }

    private ProductPhotoService BuildRealPhotoService(Mock<IObjectStorage> storageMock)
    {
        Mock<IHttpClientFactory> httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock
            .Setup(f => f.CreateClient(ProductPhotoService.HttpClientName))
            .Returns(new HttpClient());

        Mock<ITenantContext> tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(tc => tc.TenantId).Returns(_tenantId);

        return new ProductPhotoService(
            storageMock.Object,
            httpClientFactoryMock.Object,
            tenantContextMock.Object,
            Mock.Of<ILogger<ProductPhotoService>>(),
            TimeProvider.System);
    }

    private CatalogDbContext BuildCatalogDbContext(Guid tenantId, string databaseName)
    {
        DbContextOptions<CatalogDbContext> options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(databaseName: databaseName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        Mock<ITenantContext> tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(tc => tc.TenantId).Returns(tenantId);

        CatalogDbContext context = new CatalogDbContext(options, tenantContextMock.Object);
        return context;
    }

    private async Task CreateChannelAsync(CatalogDbContext context, Guid tenantId)
    {
        Channel channel = BuildChannel();
        await context.Channels.AddAsync(channel);

        // Р В Р’В Р РЋРЎС™Р В Р’В Р Р†Р вЂљРЎС› Р В Р’В Р В РІР‚В Р В Р Р‹Р В РЎвЂњР В Р Р‹Р Р†Р вЂљРЎв„ўР В Р’В Р вЂ™Р’В°Р В Р’В Р В РІР‚В¦Р В Р’В Р РЋРІР‚СћР В Р’В Р В РІР‚В Р В Р’В Р вЂ™Р’В»Р В Р Р‹Р В РІР‚в„–Р В Р Р‹Р Р†Р вЂљРЎСљР В Р’В Р РЋР’ВР В Р’В Р РЋРІР‚Сћ TenantId Р В Р Р‹Р В Р РЏР В Р’В Р В РІР‚В Р В Р’В Р В РІР‚В¦Р В Р’В Р РЋРІР‚Сћ - SaveChanges Р В Р’В Р вЂ™Р’В·Р В Р Р‹Р В РІР‚С™Р В Р’В Р РЋРІР‚СћР В Р’В Р вЂ™Р’В±Р В Р’В Р РЋРІР‚ВР В Р Р‹Р Р†Р вЂљРЎв„ўР В Р Р‹Р В Р вЂ° Р В Р Р‹Р Р†Р вЂљР’В Р В Р’В Р вЂ™Р’Вµ Р В Р’В Р вЂ™Р’В°Р В Р’В Р В РІР‚В Р В Р Р‹Р Р†Р вЂљРЎв„ўР В Р’В Р РЋРІР‚СћР В Р’В Р РЋР’ВР В Р’В Р вЂ™Р’В°Р В Р Р‹Р Р†Р вЂљРЎв„ўР В Р’В Р РЋРІР‚ВР В Р Р‹Р Р†Р вЂљР Р‹Р В Р’В Р В РІР‚В¦Р В Р’В Р РЋРІР‚Сћ
        // context.Entry(channel).Property("TenantId").CurrentValue = tenantId;

        await context.SaveChangesAsync();
    }

    private Channel BuildChannel()
    {
        return BuildChannel(_channelId, "Wildberries");
    }

    private static Channel BuildChannel(Guid channelId, string name)
    {
        return new Channel
        {
            Id = channelId,
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private ProductBatchImportRequestedEvent BuildMessage(ChannelProductCard card)
    {
        return BuildMessage(card, _channelId);
    }

    private ProductBatchImportRequestedEvent BuildMessage(ChannelProductCard card, Guid channelId)
    {
        return new ProductBatchImportRequestedEvent
        {
            JobRunId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = channelId,
            Cards = new List<ChannelProductCard> { card },
            IsLastBatch = false
        };
    }

    private static Product BuildSeededProduct(string code, string sku, string name)
    {
        return new Product
        {
            Id = Guid.NewGuid(),
            Code = code,
            Sku = sku,
            Name = name,
            Status = ProductStatus.Active,
            CreationSource = CreationSource.Manual,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10)
        };
    }

    private static ChannelProduct BuildSeededLink(
        Guid productId,
        Guid channelId,
        string externalProductId,
        string externalSku)
    {
        return new ChannelProduct
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            ChannelId = channelId,
            ExternalProductId = externalProductId,
            ExternalSku = externalSku,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10)
        };
    }

    private static ProductBarcode BuildSeededBarcode(Guid productId, string value)
    {
        return new ProductBarcode
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            Value = value,
            Type = ChannelCodes.Wildberries,
            IsDefault = false,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10)
        };
    }

    private ProductBatchImporter BuildImporter(CatalogDbContext context, IPublishEndpoint publishEndpoint)
    {
        return BuildImporter(context, publishEndpoint, BuildPhotoServiceMock());
    }

    private ProductBatchImporter BuildImporter(
        CatalogDbContext context,
        IPublishEndpoint publishEndpoint,
        Mock<IProductPhotoService> photoService)
    {
        ProductImportStore store = new ProductImportStore(context);
        MassTransitEventPublisher eventPublisher = new MassTransitEventPublisher(publishEndpoint);
        Mock<ILogger<ProductBatchImporter>> loggerMock = new Mock<ILogger<ProductBatchImporter>>();

        return new ProductBatchImporter(
            store,
            photoService.Object,
            eventPublisher,
            new FakeNumberingSchemeProvider(),
            new FakeNumberAllocator(),
            loggerMock.Object,
            TimeProvider.System);
    }

    private static Mock<IProductPhotoService> BuildPhotoServiceMock()
    {
        Mock<IProductPhotoService> photoServiceMock = new Mock<IProductPhotoService>();
        photoServiceMock
            .Setup(s => s.ImportPhotosAsync(
                It.IsAny<Product>(),
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyList<MarketplacePhotoSource>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductPhotoImportResult());

        return photoServiceMock;
    }

    private static ChannelProductCard BuildCardWithPhotos()
    {
        return new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Test Product",
            OfferId = "SKU-001",
            Barcodes = new List<string> { "1234567890123" },
            Description = "Test Description",
            Photos = new List<ChannelProductPhoto>
            {
                new ChannelProductPhoto { Url = "https://cdn.wb.ru/photo-thumb.webp", SizeVariant = "thumbnail", Width = 246, Height = 328, DisplayOrder = 1 }, new ChannelProductPhoto { Url = "https://cdn.wb.ru/photo-large.webp", SizeVariant = "large", Width = 516, Height = 688, DisplayOrder = 1 },
            },
        };
    }
}
