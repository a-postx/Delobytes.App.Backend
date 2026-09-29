using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Catalog.Infrastructure.Messaging.Consumers;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure;

/// <summary>
/// Integration tests for ImportProductBatchConsumer.
/// Tests idempotent batch processing, product matching, barcode synchronization, and tenant isolation.
/// </summary>
public class ImportProductBatchConsumerTests
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
        ImportProductBatchConsumer consumer = BuildConsumer(consumerContext, publishEndpointMock.Object);

        WildberriesCardSnapshot card = new WildberriesCardSnapshot
        {
            NmId = 123456789,
            Name = "Test Product",
            VendorCode = "SKU-001",
            Barcodes = new List<string> { "1234567890123" },
            Description = "Test Description"
        };

        ProductImportBatchRequestedEvent message = new ProductImportBatchRequestedEvent
        {
            SyncJobId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<WildberriesCardSnapshot> { card },
            IsLastBatch = false
        };

        // Act
        await consumer.ProcessAsync(message, CancellationToken.None);

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
        product.Barcodes.First().Type.Should().Be("WB");

        product.ChannelProducts.Should().HaveCount(1);
        ChannelProduct channelProduct = product.ChannelProducts.First();
        channelProduct.ExternalProductId.Should().Be("123456789");
        channelProduct.ExternalSku.Should().Be("SKU-001");
        channelProduct.IsActive.Should().BeTrue();
        channelProduct.ChannelId.Should().Be(_channelId);

        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ProductImportBatchCompletedEvent>(e =>
                    e.SyncJobId == _syncJobId &&
                    e.RecordsProcessed == 1 &&
                    e.RecordsCreated == 1 &&
                    e.RecordsUpdated == 0 &&
                    e.RecordsSkipped == 0 &&
                    e.RecordsFailed == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_UpdatesExistingProduct_WhenChannelProductExists()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        Product existingProduct = new Product
        {
            Id = Guid.NewGuid(),
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

        existingProduct.ChannelProducts.Add(existingChannelProduct);
        await setupContext.Products.AddAsync(existingProduct);
        await setupContext.SaveChangesAsync();

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);

        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ImportProductBatchConsumer consumer = BuildConsumer(consumerContext, publishEndpointMock.Object);

        WildberriesCardSnapshot card = new WildberriesCardSnapshot
        {
            NmId = 123456789,
            Name = "Updated Name",
            VendorCode = "SKU-001",
            Barcodes = new List<string> { "1234567890123" },
            Description = "Updated Description"
        };

        ProductImportBatchRequestedEvent message = new ProductImportBatchRequestedEvent
        {
            SyncJobId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<WildberriesCardSnapshot> { card },
            IsLastBatch = false
        };

        // Act
        await consumer.ProcessAsync(message, CancellationToken.None);

        // Assert
        Product? product = await consumerContext.Products
            .IgnoreQueryFilters()
            .Include(p => p.Barcodes)
            .Include(p => p.ChannelProducts)
            .FirstOrDefaultAsync(p => p.Id == existingProduct.Id);

        product.Should().NotBeNull();
        product!.Name.Should().Be("Updated Name");
        product.Description.Should().Be("Updated Description");

        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ProductImportBatchCompletedEvent>(e =>
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
        ImportProductBatchConsumer consumer = BuildConsumer(consumerContext, publishEndpointMock.Object);

        WildberriesCardSnapshot card = new WildberriesCardSnapshot
        {
            NmId = 123456789,
            Name = "Test Product",
            VendorCode = "SKU-001",
            Barcodes = new List<string> { "1234567890123" },
            Description = "Test Description"
        };

        ProductImportBatchRequestedEvent message = new ProductImportBatchRequestedEvent
        {
            SyncJobId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<WildberriesCardSnapshot> { card },
            IsLastBatch = false
        };

        // Act - Process the same batch twice
        await consumer.ProcessAsync(message, CancellationToken.None);
        await consumer.ProcessAsync(message, CancellationToken.None);

        // Assert
        List<Product> products = await consumerContext.Products
            .IgnoreQueryFilters()
            .Include(p => p.Barcodes)
            .Include(p => p.ChannelProducts)
            .Where(p => p.Sku == "SKU-001")
            .ToListAsync();

        products.Should().HaveCount(1);
        products[0].ChannelProducts.Should().HaveCount(1);
        products[0].Barcodes.Where(b => b.Type == "WB").Should().HaveCount(1);

        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ProductImportBatchCompletedEvent>(e =>
                    e.RecordsProcessed == 1 &&
                    e.RecordsCreated == 1 &&
                    e.RecordsUpdated == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);

        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ProductImportBatchCompletedEvent>(e =>
                    e.RecordsProcessed == 1 &&
                    e.RecordsCreated == 0 &&
                    e.RecordsSkipped == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_LinksToExistingProduct_WhenVendorCodeMatches()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        Product existingProduct = new Product
        {
            Id = Guid.NewGuid(),
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
        ImportProductBatchConsumer consumer = BuildConsumer(consumerContext, publishEndpointMock.Object);

        WildberriesCardSnapshot card = new WildberriesCardSnapshot
        {
            NmId = 123456789,
            Name = "Product from WB",
            VendorCode = "SKU-001",
            Barcodes = new List<string> { "1234567890123" }
        };

        ProductImportBatchRequestedEvent message = new ProductImportBatchRequestedEvent
        {
            SyncJobId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<WildberriesCardSnapshot> { card },
            IsLastBatch = false
        };

        // Act
        await consumer.ProcessAsync(message, CancellationToken.None);

        // Assert
        List<Product> products = await consumerContext.Products
            .IgnoreQueryFilters()
            .Include(p => p.ChannelProducts)
            .Where(p => p.Sku == "SKU-001")
            .ToListAsync();

        products.Should().HaveCount(1);
        products[0].Id.Should().Be(existingProduct.Id);
        products[0].CreationSource.Should().Be(CreationSource.Manual);
        products[0].ChannelProducts.Should().HaveCount(1);
        products[0].ChannelProducts.First().ExternalProductId.Should().Be("123456789");
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
        ImportProductBatchConsumer consumer = BuildConsumer(consumerContext, publishEndpointMock.Object);

        WildberriesCardSnapshot card = new WildberriesCardSnapshot
        {
            NmId = 123456789,
            Name = "Product from WB",
            VendorCode = "WB-SKU-001",
            Barcodes = new List<string> { "9876543210987", "1111111111111" }
        };

        ProductImportBatchRequestedEvent message = new ProductImportBatchRequestedEvent
        {
            SyncJobId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<WildberriesCardSnapshot> { card },
            IsLastBatch = false
        };

        // Act
        await consumer.ProcessAsync(message, CancellationToken.None);

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
        products[0].Barcodes.Where(b => b.Type == "WB").Should().HaveCount(2);
    }

    [Fact]
    public async Task ProcessAsync_SkipsLinking_WhenMultipleVendorCodeMatchesExist()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildCatalogDbContext(_tenantId, databaseName);
        await CreateChannelAsync(setupContext, _tenantId);

        Product product1 = new Product
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-001",
            Name = "Product 1",
            Status = ProductStatus.Active,
            CreationSource = CreationSource.Manual,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10)
        };

        Product product2 = new Product
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-001",
            Name = "Product 2",
            Status = ProductStatus.Active,
            CreationSource = CreationSource.Manual,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-5)
        };

        await setupContext.Products.AddRangeAsync(product1, product2);
        await setupContext.SaveChangesAsync();

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);

        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ImportProductBatchConsumer consumer = BuildConsumer(consumerContext, publishEndpointMock.Object);

        WildberriesCardSnapshot card = new WildberriesCardSnapshot
        {
            NmId = 123456789,
            Name = "Ambiguous Product",
            VendorCode = "SKU-001",
            Barcodes = new List<string>()
        };

        ProductImportBatchRequestedEvent message = new ProductImportBatchRequestedEvent
        {
            SyncJobId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<WildberriesCardSnapshot> { card },
            IsLastBatch = false
        };

        // Act
        await consumer.ProcessAsync(message, CancellationToken.None);

        // Assert
        List<Product> allProducts = await consumerContext.Products
            .IgnoreQueryFilters()
            .Include(p => p.ChannelProducts)
            .ToListAsync();

        allProducts.Should().HaveCount(3);

        Product? newProduct = allProducts.FirstOrDefault(p => p.CreationSource == CreationSource.WildberriesImport);
        newProduct.Should().NotBeNull();
        newProduct!.Sku.Should().Be("SKU-001");
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
        ImportProductBatchConsumer consumer = BuildConsumer(consumerContext, publishEndpointMock.Object);

        WildberriesCardSnapshot card = new WildberriesCardSnapshot
        {
            NmId = 123456789,
            Name = "Product",
            VendorCode = "SKU-001",
            Barcodes = new List<string> { "1111111111111", "2222222222222" }
        };

        ProductImportBatchRequestedEvent message = new ProductImportBatchRequestedEvent
        {
            SyncJobId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<WildberriesCardSnapshot> { card },
            IsLastBatch = false
        };

        // Act
        await consumer.ProcessAsync(message, CancellationToken.None);

        // Assert
        Product? product = await consumerContext.Products
            .IgnoreQueryFilters()
            .Include(p => p.Barcodes)
            .FirstOrDefaultAsync(p => p.Id == existingProduct.Id);

        product.Should().NotBeNull();
        product!.Barcodes.Where(b => b.Type == "WB").Should().HaveCount(2);
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

        ImportProductBatchConsumer consumerTenant1 = BuildConsumer(consumerContext1, publishEndpointMock1.Object);
        ImportProductBatchConsumer consumerTenant2 = BuildConsumer(consumerContext2, publishEndpointMock2.Object);

        WildberriesCardSnapshot card = new WildberriesCardSnapshot
        {
            NmId = 123456789,
            Name = "Shared Product",
            VendorCode = "SKU-001",
            Barcodes = new List<string> { "1234567890123" }
        };

        ProductImportBatchRequestedEvent messageTenant1 = new ProductImportBatchRequestedEvent
        {
            SyncJobId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<WildberriesCardSnapshot> { card },
            IsLastBatch = false
        };

        ProductImportBatchRequestedEvent messageTenant2 = new ProductImportBatchRequestedEvent
        {
            SyncJobId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<WildberriesCardSnapshot> { card },
            IsLastBatch = false
        };

        // Act
        await consumerTenant1.ProcessAsync(messageTenant1, CancellationToken.None);
        await consumerTenant2.ProcessAsync(messageTenant2, CancellationToken.None);

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
        ImportProductBatchConsumer consumer = BuildConsumer(consumerContext, publishEndpointMock.Object);

        WildberriesCardSnapshot card = new WildberriesCardSnapshot
        {
            NmId = 123456789,
            Name = "Product Without Channel",
            VendorCode = "SKU-001",
            Barcodes = new List<string>()
        };

        ProductImportBatchRequestedEvent message = new ProductImportBatchRequestedEvent
        {
            SyncJobId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<WildberriesCardSnapshot> { card },
            IsLastBatch = false
        };

        // Act
        await consumer.ProcessAsync(message, CancellationToken.None);

        // Assert
        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ProductImportBatchCompletedEvent>(e =>
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
            ExternalProductId = "123456789", // nmID остаётся тем же
            ExternalSku = "OLD-VENDOR-CODE",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        existingProduct.ChannelProducts.Add(existingChannelProduct);
        await setupContext.Products.AddAsync(existingProduct);
        await setupContext.SaveChangesAsync();

        CatalogDbContext consumerContext = BuildCatalogDbContext(_tenantId, databaseName);

        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ImportProductBatchConsumer consumer = BuildConsumer(consumerContext, publishEndpointMock.Object);

        // Новая карточка с тем же nmID, но другим vendorCode
        WildberriesCardSnapshot card = new WildberriesCardSnapshot
        {
            NmId = 123456789,
            Name = "Updated Product Name",
            VendorCode = "NEW-VENDOR-CODE", // Изменился!
            Barcodes = new List<string> { "1234567890123" },
            Description = "Updated Description"
        };

        ProductImportBatchRequestedEvent message = new ProductImportBatchRequestedEvent
        {
            SyncJobId = _syncJobId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<WildberriesCardSnapshot> { card },
            IsLastBatch = false
        };

        // Act
        await consumer.ProcessAsync(message, CancellationToken.None);

        // Assert
        Product? product = await consumerContext.Products
            .IgnoreQueryFilters()
            .Include(p => p.ChannelProducts)
            .FirstOrDefaultAsync(p => p.Id == existingProduct.Id);

        product.Should().NotBeNull();
        product!.Sku.Should().Be("OLD-VENDOR-CODE", "Product.Sku не обновляется при импорте");
        product.Name.Should().Be("Updated Product Name");
        product.Description.Should().Be("Updated Description");

        ChannelProduct? channelProduct = product.ChannelProducts.FirstOrDefault();
        channelProduct.Should().NotBeNull();
        channelProduct!.ExternalProductId.Should().Be("123456789", "nmID должен остаться неизменным");
        channelProduct.ExternalSku.Should().Be("NEW-VENDOR-CODE", "ExternalSku должен обновиться на новый vendorCode");

        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ProductImportBatchCompletedEvent>(e =>
                    e.RecordsProcessed == 1 &&
                    e.RecordsCreated == 0 &&
                    e.RecordsUpdated == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
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

        // НЕ встановлюємо TenantId явно - SaveChanges зробить це автоматично
        // context.Entry(channel).Property("TenantId").CurrentValue = tenantId;

        await context.SaveChangesAsync();
    }

    private Channel BuildChannel()
    {
        return new Channel
        {
            Id = _channelId,
            Name = "Wildberries",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private ImportProductBatchConsumer BuildConsumer(CatalogDbContext context, IPublishEndpoint publishEndpoint)
    {
        Mock<ILogger<ImportProductBatchConsumer>> loggerMock = new Mock<ILogger<ImportProductBatchConsumer>>();
        return new ImportProductBatchConsumer(context, publishEndpoint, loggerMock.Object);
    }
}
