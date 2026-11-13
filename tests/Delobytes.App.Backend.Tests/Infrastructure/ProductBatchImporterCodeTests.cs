using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Application.Products;
using Delobytes.App.Backend.Catalog.Application.Services;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Catalog.Infrastructure.Messaging;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Contracts.Interfaces;
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
/// A product created by the import receives its immutable code from the numbering scheme, and a
/// card that only refreshes an existing link consumes no number at all.
/// </summary>
public class ProductBatchImporterCodeTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _channelId = Guid.NewGuid();
    private readonly Guid _connectionId = Guid.NewGuid();
    private readonly Guid _jobRunId = Guid.NewGuid();

    [Fact]
    public async Task ImportAsync_NewCard_AssignsCodeFromTheNumberingScheme()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        await SeedChannelAsync(databaseName);

        FakeNumberAllocator allocator = new FakeNumberAllocator();
        FakeNumberingSchemeProvider schemeProvider = new FakeNumberingSchemeProvider();
        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();

        CatalogDbContext consumerContext = BuildContext(databaseName);
        ProductBatchImporter importer = BuildImporter(
            consumerContext,
            publishEndpointMock.Object,
            schemeProvider,
            allocator);

        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Product with a code",
            OfferId = "SKU-001",
            Barcodes = new List<string> { "1234567890123" },
        };

        // Act
        await importer.ImportAsync(BuildMessage(card), CancellationToken.None);

        // Assert
        List<Product> products = await consumerContext.Products
            .IgnoreQueryFilters()
            .ToListAsync();

        products.Should().ContainSingle();
        products[0].Code.Should().Be("P-000001");
        products[0].Sku.Should().Be("SKU-001");
        schemeProvider.CallCount.Should().Be(1);
        allocator.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ImportAsync_KnownLink_DoesNotAllocateACode()
    {
        // Arrange: the card is an update of an existing link, so no product is created and the
        // counter must not move.
        string databaseName = Guid.NewGuid().ToString();
        await SeedChannelAsync(databaseName);
        await SeedLinkedProductAsync(databaseName);

        FakeNumberAllocator allocator = new FakeNumberAllocator();
        FakeNumberingSchemeProvider schemeProvider = new FakeNumberingSchemeProvider();
        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();

        CatalogDbContext consumerContext = BuildContext(databaseName);
        ProductBatchImporter importer = BuildImporter(
            consumerContext,
            publishEndpointMock.Object,
            schemeProvider,
            allocator);

        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Renamed product",
            OfferId = "SKU-001",
            Barcodes = new List<string> { "1234567890123" },
        };

        // Act
        await importer.ImportAsync(BuildMessage(card), CancellationToken.None);

        // Assert
        allocator.CallCount.Should().Be(0);
        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ProductBatchImportCompletedEvent>(e => e.RecordsUpdated == 1 && e.RecordsCreated == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private async Task SeedChannelAsync(string databaseName)
    {
        CatalogDbContext context = BuildContext(databaseName);
        await context.Channels.AddAsync(new Channel
        {
            Id = _channelId,
            Name = "Wildberries",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();
    }

    private async Task SeedLinkedProductAsync(string databaseName)
    {
        CatalogDbContext context = BuildContext(databaseName);

        Product product = new Product
        {
            Id = Guid.NewGuid(),
            Code = "P-000001",
            Sku = "SKU-001",
            Name = "Existing product",
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

        await context.Products.AddAsync(product);
        await context.SaveChangesAsync();
    }

    private ProductBatchImportRequestedEvent BuildMessage(ChannelProductCard card)
    {
        return new ProductBatchImportRequestedEvent
        {
            JobRunId = _jobRunId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = new List<ChannelProductCard> { card },
            IsLastBatch = false,
        };
    }

    private CatalogDbContext BuildContext(string databaseName)
    {
        DbContextOptions<CatalogDbContext> options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        Mock<ITenantContext> tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(tc => tc.TenantId).Returns(_tenantId);

        return new CatalogDbContext(options, tenantContextMock.Object);
    }

    private static ProductBatchImporter BuildImporter(
        CatalogDbContext context,
        IPublishEndpoint publishEndpoint,
        FakeNumberingSchemeProvider schemeProvider,
        FakeNumberAllocator allocator)
    {
        Mock<IProductPhotoService> photoServiceMock = new Mock<IProductPhotoService>();
        photoServiceMock
            .Setup(s => s.ImportPhotosAsync(
                It.IsAny<Product>(),
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyList<MarketplacePhotoSource>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductPhotoImportResult());

        return new ProductBatchImporter(
            new ProductImportStore(context),
            photoServiceMock.Object,
            new MassTransitEventPublisher(publishEndpoint),
            schemeProvider,
            allocator,
            Mock.Of<ILogger<ProductBatchImporter>>(),
            TimeProvider.System);
    }
}
