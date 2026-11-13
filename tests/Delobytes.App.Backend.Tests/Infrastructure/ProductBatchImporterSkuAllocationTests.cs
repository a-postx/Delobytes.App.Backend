using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Application.Products;
using Delobytes.App.Backend.Catalog.Application.Services;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Catalog.Infrastructure.Messaging;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Contracts.Errors;
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
/// The internal SKU of an imported product comes from the card's vendor code when that value is
/// free, and from the newly issued product code otherwise. A code that is itself already someone's
/// SKU is skipped, and five such collisions fail that card alone.
/// </summary>
public class ProductBatchImporterSkuAllocationTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _channelId = Guid.NewGuid();
    private readonly Guid _connectionId = Guid.NewGuid();
    private readonly Guid _jobRunId = Guid.NewGuid();

    [Fact]
    public async Task ImportAsync_VendorCodeTakenAsSku_CreatesProductWithCodeAsSku()
    {
        // Arrange: a hand-made product already owns the vendor code, and no channel link or
        // barcode matches the card, so the card becomes a new product with a free SKU.
        string databaseName = Guid.NewGuid().ToString();
        await SeedChannelAsync(databaseName);
        await SeedProductsAsync(databaseName, ("TAKEN-SKU", "P-000100"));

        FakeNumberAllocator allocator = new FakeNumberAllocator();
        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();

        CatalogDbContext consumerContext = BuildContext(databaseName);
        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object, allocator);

        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Product with a taken vendor code",
            OfferId = "TAKEN-SKU",
            Barcodes = new List<string>(),
        };

        // Act
        await importer.ImportAsync(BuildMessage(card), CancellationToken.None);

        // Assert
        Product created = await GetSingleCreatedProductAsync(consumerContext);
        created.Sku.Should().Be("P-000001");
        created.Code.Should().Be("P-000001");
        allocator.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ImportAsync_FirstCodeIsTakenAsSku_AllocatesTheNextCode()
    {
        // Arrange: Product.Sku is user-editable, so a number typed in by hand can equal the code
        // the allocator is about to issue. The next number must be used instead.
        string databaseName = Guid.NewGuid().ToString();
        await SeedChannelAsync(databaseName);
        await SeedProductsAsync(databaseName, ("TAKEN-SKU", "P-000050"), ("P-000001", "P-000051"));

        FakeNumberAllocator allocator = new FakeNumberAllocator();
        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();

        CatalogDbContext consumerContext = BuildContext(databaseName);
        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object, allocator);

        ChannelProductCard card = new ChannelProductCard
        {
            ExternalProductId = "123456789",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Product needing a second attempt",
            OfferId = "TAKEN-SKU",
            Barcodes = new List<string>(),
        };

        // Act
        await importer.ImportAsync(BuildMessage(card), CancellationToken.None);

        // Assert
        Product created = await GetSingleCreatedProductAsync(consumerContext);
        created.Code.Should().Be("P-000002");
        created.Sku.Should().Be("P-000002");
        allocator.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task ImportAsync_FiveConsecutiveCodeCollisions_FailThatCardOnly()
    {
        // Arrange: every code the allocator can issue is already a SKU. The card cannot obtain a
        // free value and must fail on its own savepoint, while a sibling card still imports.
        string databaseName = Guid.NewGuid().ToString();
        await SeedChannelAsync(databaseName);
        await SeedProductsAsync(
            databaseName,
            ("TAKEN-SKU", "P-000090"),
            ("P-000001", "P-000091"),
            ("P-000002", "P-000092"),
            ("P-000003", "P-000093"),
            ("P-000004", "P-000094"),
            ("P-000005", "P-000095"));

        FakeNumberAllocator allocator = new FakeNumberAllocator();
        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();

        CatalogDbContext consumerContext = BuildContext(databaseName);
        ProductBatchImporter importer = BuildImporter(consumerContext, publishEndpointMock.Object, allocator);

        ChannelProductCard failingCard = new ChannelProductCard
        {
            ExternalProductId = "111111111",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Product that cannot get a code",
            OfferId = "TAKEN-SKU",
            Barcodes = new List<string>(),
        };

        ChannelProductCard succeedingCard = new ChannelProductCard
        {
            ExternalProductId = "222222222",
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Product with a free vendor code",
            OfferId = "FREE-SKU",
            Barcodes = new List<string>(),
        };

        // Act
        await importer.ImportAsync(BuildMessage(failingCard, succeedingCard), CancellationToken.None);

        // Assert
        List<Product> products = await consumerContext.Products
            .IgnoreQueryFilters()
            .ToListAsync();

        products.Should().HaveCount(7, "only the sibling card may add a product");
        products.Should().ContainSingle(p => p.Sku == "FREE-SKU");
        products.Should().NotContain(p => p.Name == "Product that cannot get a code");
        allocator.CallCount.Should().Be(6, "five rejected numbers plus one for the sibling card");

        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ProductBatchImportCompletedEvent>(e =>
                    e.RecordsProcessed == 2
                    && e.RecordsCreated == 1
                    && e.RecordsFailed == 1
                    && e.ErrorMessage != null
                    && e.ErrorMessage.Contains(
                        ErrorCodes.Catalog.ProductCodeAllocationFailed.DefaultMessage,
                        StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static async Task<Product> GetSingleCreatedProductAsync(CatalogDbContext context)
    {
        List<Product> products = await context.Products
            .IgnoreQueryFilters()
            .Where(p => p.CreationSource == CreationSource.WildberriesImport)
            .ToListAsync();

        return products.Should().ContainSingle().Subject;
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

    private async Task SeedProductsAsync(string databaseName, params (string Sku, string Code)[] products)
    {
        CatalogDbContext context = BuildContext(databaseName);

        foreach ((string sku, string code) in products)
        {
            await context.Products.AddAsync(new Product
            {
                Id = Guid.NewGuid(),
                Code = code,
                Sku = sku,
                Name = "Seeded product " + sku,
                Status = ProductStatus.Active,
                CreationSource = CreationSource.Manual,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            });
        }

        await context.SaveChangesAsync();
    }

    private ProductBatchImportRequestedEvent BuildMessage(params ChannelProductCard[] cards)
    {
        return new ProductBatchImportRequestedEvent
        {
            JobRunId = _jobRunId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = cards.ToList(),
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
            new FakeNumberingSchemeProvider(),
            allocator,
            Mock.Of<ILogger<ProductBatchImporter>>(),
            TimeProvider.System);
    }
}
