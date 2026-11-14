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
/// The two barcode sets of an imported card and how they differ. <see cref="ProductBarcode"/> is the
/// catalogue's identity for a product: unique per tenant, written once when the import creates the
/// product and never rewritten afterwards. <see cref="ChannelProductBarcode"/> is the channel's copy:
/// owned by the channel, replaced on every import, and allowed to repeat across the cards of one
/// channel, which is exactly the case that used to fail the whole run on the canonical unique index.
/// </summary>
public class ProductBatchImporterBarcodeTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _channelId = Guid.NewGuid();
    private readonly Guid _connectionId = Guid.NewGuid();
    private readonly Guid _jobRunId = Guid.NewGuid();

    [Fact]
    public async Task ImportAsync_NewCard_WritesBothBarcodeSets()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        await SeedChannelAsync(databaseName);

        CatalogDbContext context = BuildContext(databaseName);
        ProductBatchImporter importer = BuildImporter(context, new Mock<IPublishEndpoint>().Object);

        ChannelProductCard card = BuildCard("123456789", "SKU-001", "4600000000001", "4600000000002");

        // Act
        await importer.ImportAsync(BuildMessage(card, _channelId), CancellationToken.None);

        // Assert
        Product product = (await context.Products.IgnoreQueryFilters().Include(p => p.Barcodes).ToListAsync())
            .Should().ContainSingle().Subject;

        product.Barcodes.Select(b => b.Value).Should().BeEquivalentTo(new[] { "4600000000001", "4600000000002" });
        product.Barcodes.Should().OnlyHaveUniqueItems(b => b.Value);
        product.Barcodes.Single(b => b.Value == "4600000000001").IsDefault
            .Should().BeTrue("the first canonical barcode is the one printed on the label");
        product.Barcodes.Single(b => b.Value == "4600000000002").IsDefault.Should().BeFalse();

        List<ChannelProductBarcode> channelBarcodes = await GetChannelBarcodesAsync(context);
        channelBarcodes.Select(b => b.Value).Should().BeEquivalentTo(new[] { "4600000000001", "4600000000002" });
        channelBarcodes.Should().OnlyContain(b => b.Type == ChannelCodes.Wildberries);
    }

    [Fact]
    public async Task ImportAsync_CardLinkedToUnlinkedProduct_LeavesCanonicalBarcodesUntouched()
    {
        // Arrange: the product exists and owns one canonical barcode; the card reports it plus a
        // second value the catalogue has never seen.
        string databaseName = Guid.NewGuid().ToString();
        await SeedChannelAsync(databaseName);
        await SeedProductWithCanonicalBarcodeAsync(databaseName, "P-000900", "INTERNAL-SKU", "4600000000001");

        CatalogDbContext context = BuildContext(databaseName);
        ProductBatchImporter importer = BuildImporter(context, new Mock<IPublishEndpoint>().Object);

        ChannelProductCard card = BuildCard("777000111", "CARD-ART", "4600000000001", "4600000000009");

        // Act
        await importer.ImportAsync(BuildMessage(card, _channelId), CancellationToken.None);

        // Assert
        Product product = (await context.Products.IgnoreQueryFilters().Include(p => p.Barcodes).ToListAsync())
            .Should().ContainSingle().Subject;

        // A link must not extend the canonical set.
        product.Barcodes.Select(b => b.Value).Should().Equal("4600000000001");

        (await GetChannelBarcodesAsync(context)).Select(b => b.Value)
            .Should().BeEquivalentTo(new[] { "4600000000001", "4600000000009" },
                "the channel keeps reporting both values on its own side");
    }

    [Fact]
    public async Task ImportAsync_KnownCard_ReplacesTheChannelBarcodeSet()
    {
        // Arrange: the card on the channel currently reports A and B; the next sync reports B and C.
        string databaseName = Guid.NewGuid().ToString();
        await SeedChannelAsync(databaseName);
        await SeedLinkedProductAsync(databaseName, "P-000901", "SKU-001", "123456789", "1111111111111", "2222222222222");

        CatalogDbContext context = BuildContext(databaseName);
        ProductBatchImporter importer = BuildImporter(context, new Mock<IPublishEndpoint>().Object);

        ChannelProductCard card = BuildCard("123456789", "SKU-001", "2222222222222", "3333333333333");

        // Act
        await importer.ImportAsync(BuildMessage(card, _channelId), CancellationToken.None);

        // Assert
        (await GetChannelBarcodesAsync(context)).Select(b => b.Value)
            .Should().BeEquivalentTo(new[] { "2222222222222", "3333333333333" },
                "the channel's set is replaced, not accumulated");

        (await context.Products.IgnoreQueryFilters().Include(p => p.Barcodes).SingleAsync()).Barcodes
            .Should().BeEmpty("an update never writes canonical barcodes");
    }

    [Fact]
    public async Task ImportAsync_KnownCardWithoutBarcodes_KeepsTheStoredChannelSet()
    {
        // Arrange: an empty barcode list is indistinguishable from a response that omitted the field,
        // so it must not be read as "the channel dropped everything".
        string databaseName = Guid.NewGuid().ToString();
        await SeedChannelAsync(databaseName);
        await SeedLinkedProductAsync(databaseName, "P-000902", "SKU-001", "123456789", "1111111111111", "2222222222222");

        CatalogDbContext context = BuildContext(databaseName);
        ProductBatchImporter importer = BuildImporter(context, new Mock<IPublishEndpoint>().Object);

        ChannelProductCard card = BuildCard("123456789", "SKU-001");

        // Act
        await importer.ImportAsync(BuildMessage(card, _channelId), CancellationToken.None);

        // Assert
        (await GetChannelBarcodesAsync(context)).Select(b => b.Value)
            .Should().BeEquivalentTo(new[] { "1111111111111", "2222222222222" });
    }

    [Fact]
    public async Task ImportAsync_TwoCardsOfOneChannelShareABarcode_NeitherFails()
    {
        // Arrange: the case that produced dozens of "barcode already linked to another product"
        // failures in production. A channel may report one barcode for two of its own cards, and the
        // canonical set must not turn that into a failed card.
        // The second card is reported as Skipped, not Updated: it matches the first card's product by
        // the shared barcode and that product already holds a link on this channel, which the unique
        // index on (ProductId, ChannelId) forbids duplicating. Nothing fails, but the second product
        // is not created either.
        string databaseName = Guid.NewGuid().ToString();
        await SeedChannelAsync(databaseName);

        CatalogDbContext context = BuildContext(databaseName);
        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ProductBatchImporter importer = BuildImporter(context, publishEndpointMock.Object);

        ChannelProductCard firstCard = BuildCard("100001", "ART-1", "4600000000009");
        ChannelProductCard secondCard = BuildCard("100002", "ART-2", "4600000000009");

        // Act
        await importer.ImportAsync(
            BuildMessage(new[] { firstCard, secondCard }, _channelId),
            CancellationToken.None);

        // Assert
        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ProductBatchImportCompletedEvent>(e =>
                    e.RecordsFailed == 0 &&
                    e.RecordsCreated == 1 &&
                    e.RecordsSkipped == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);

        (await context.Products.IgnoreQueryFilters().Include(p => p.Barcodes).ToListAsync())
            .Should().ContainSingle()
            .Which.Barcodes.Should().ContainSingle("the canonical set holds the value once");
    }

    [Fact]
    public async Task ImportAsync_SameBarcodeInTwoChannels_KeepsBothChannelSets()
    {
        // Arrange: the WB card creates the product; the Ozon card reports the same value.
        string databaseName = Guid.NewGuid().ToString();
        Guid secondChannelId = Guid.NewGuid();

        await SeedChannelAsync(databaseName);
        await SeedChannelAsync(databaseName, secondChannelId, ChannelCodes.Ozon);

        CatalogDbContext context = BuildContext(databaseName);
        ProductBatchImporter importer = BuildImporter(context, new Mock<IPublishEndpoint>().Object);

        ChannelProductCard wildberriesCard = BuildCard("111000111", "ART-WB", "4600000000009");
        ChannelProductCard ozonCard = BuildCard("222000222", "ART-OZON", "4600000000009");

        // Act
        await importer.ImportAsync(BuildMessage(wildberriesCard, _channelId), CancellationToken.None);
        await importer.ImportAsync(BuildMessage(ozonCard, secondChannelId), CancellationToken.None);

        // Assert
        (await context.Products.IgnoreQueryFilters().Include(p => p.Barcodes).ToListAsync())
            .Should().ContainSingle("the shared barcode identifies one product")
            .Which.Barcodes.Should().ContainSingle("and still only one canonical row");

        List<ChannelProductBarcode> channelBarcodes = await GetChannelBarcodesAsync(context);
        channelBarcodes.Should().HaveCount(2, "each channel reports the value for its own card");
        channelBarcodes.Select(b => b.Value).Should().OnlyContain(value => value == "4600000000009");
    }

    [Fact]
    public async Task ImportAsync_CardBarcodeWithSurroundingSpaces_IsStoredTrimmed()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        await SeedChannelAsync(databaseName);

        CatalogDbContext context = BuildContext(databaseName);
        ProductBatchImporter importer = BuildImporter(context, new Mock<IPublishEndpoint>().Object);

        ChannelProductCard card = BuildCard("123456789", "SKU-001", "  4600000000009  ");

        // Act
        await importer.ImportAsync(BuildMessage(card, _channelId), CancellationToken.None);

        // Assert
        (await context.Products.IgnoreQueryFilters().Include(p => p.Barcodes).SingleAsync()).Barcodes
            .Select(b => b.Value).Should().Equal("4600000000009");

        (await GetChannelBarcodesAsync(context)).Select(b => b.Value).Should().Equal("4600000000009");
    }

    [Fact]
    public async Task ImportAsync_RepeatedBarcodeInOneCard_IsStoredOncePerSet()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        await SeedChannelAsync(databaseName);

        CatalogDbContext context = BuildContext(databaseName);
        ProductBatchImporter importer = BuildImporter(context, new Mock<IPublishEndpoint>().Object);

        ChannelProductCard card = BuildCard("123456789", "SKU-001", "4600000000009", "4600000000009", " ");

        // Act
        await importer.ImportAsync(BuildMessage(card, _channelId), CancellationToken.None);

        // Assert
        (await context.Products.IgnoreQueryFilters().Include(p => p.Barcodes).SingleAsync()).Barcodes
            .Should().ContainSingle();

        (await GetChannelBarcodesAsync(context)).Should().ContainSingle();
    }

    [Fact]
    public async Task ImportAsync_ChannelBarcodeMatch_TakesPrecedenceOverACanonicalOne()
    {
        // Arrange: the barcode is canonical for one product and is also reported by a second
        // product's card on this channel. The channel's own copy is the authoritative one here.
        string databaseName = Guid.NewGuid().ToString();
        Guid otherChannelId = Guid.NewGuid();

        await SeedChannelAsync(databaseName);
        await SeedChannelAsync(databaseName, otherChannelId, ChannelCodes.Ozon);

        Guid canonicalProductId = Guid.NewGuid();
        Guid channelProductId = Guid.NewGuid();

        using (CatalogDbContext seed = BuildContext(databaseName))
        {
            Product canonicalOwner = BuildProduct(canonicalProductId, "P-000910", "SKU-CANONICAL", "Canonical owner");
            canonicalOwner.Barcodes.Add(BuildCanonicalBarcode(canonicalProductId, "4600000000009"));
            canonicalOwner.ChannelProducts.Add(BuildLink(canonicalProductId, otherChannelId, "ozon-card"));

            Product channelOwner = BuildProduct(channelProductId, "P-000911", "SKU-CHANNEL", "Channel owner");
            ChannelProduct channelLink = BuildLink(channelProductId, _channelId, "wb-card");
            channelLink.Barcodes.Add(BuildChannelBarcode(channelLink.Id, "4600000000009"));
            channelOwner.ChannelProducts.Add(channelLink);

            seed.Products.AddRange(canonicalOwner, channelOwner);
            await seed.SaveChangesAsync();
        }

        CatalogDbContext context = BuildContext(databaseName);
        ProductBatchImporter importer = BuildImporter(context, new Mock<IPublishEndpoint>().Object);

        // The card is unknown on this channel (a different external id) and carries the shared value.
        ChannelProductCard card = BuildCard("unseen-card", "ART-X", "4600000000009");

        // Act
        await importer.ImportAsync(BuildMessage(card, _channelId), CancellationToken.None);

        // Assert: the card resolves to the channel's own product, which already has a link here, so
        // nothing is created and nothing is re-pointed.
        (await context.ChannelProducts.IgnoreQueryFilters().ToListAsync()).Should().HaveCount(2);
        (await context.ChannelProducts.IgnoreQueryFilters()
                .Where(cp => cp.ProductId == canonicalProductId && cp.ChannelId == _channelId)
                .ToListAsync())
            .Should().BeEmpty("the canonical owner must not gain a second link from this card");
    }

    [Fact]
    public async Task ImportAsync_BarcodesPointingAtTwoProducts_SkipsTheCard()
    {
        // Arrange: two values of one card each belong to a different canonical product. That is a
        // contradiction in the card, so creating a third product would duplicate one of the two.
        string databaseName = Guid.NewGuid().ToString();
        await SeedChannelAsync(databaseName);

        using (CatalogDbContext seed = BuildContext(databaseName))
        {
            Guid firstId = Guid.NewGuid();
            Guid secondId = Guid.NewGuid();

            Product first = BuildProduct(firstId, "P-000912", "SKU-A", "First");
            first.Barcodes.Add(BuildCanonicalBarcode(firstId, "1111111111111"));

            Product second = BuildProduct(secondId, "P-000913", "SKU-B", "Second");
            second.Barcodes.Add(BuildCanonicalBarcode(secondId, "2222222222222"));

            seed.Products.AddRange(first, second);
            await seed.SaveChangesAsync();
        }

        CatalogDbContext context = BuildContext(databaseName);
        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        ProductBatchImporter importer = BuildImporter(context, publishEndpointMock.Object);

        ChannelProductCard card = BuildCard("777000555", "NO-MATCH", "1111111111111", "2222222222222");

        // Act
        await importer.ImportAsync(BuildMessage(card, _channelId), CancellationToken.None);

        // Assert
        (await context.Products.IgnoreQueryFilters().CountAsync()).Should().Be(2);
        (await context.ChannelProducts.IgnoreQueryFilters().CountAsync()).Should().Be(0);

        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<ProductBatchImportCompletedEvent>(e => e.RecordsCreated == 0 && e.RecordsSkipped == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static ProductBatchImportRequestedEvent BuildMessage(ChannelProductCard card, Guid channelId)
    {
        return BuildMessage(new[] { card }, channelId);
    }

    private static ProductBatchImportRequestedEvent BuildMessage(IReadOnlyList<ChannelProductCard> cards, Guid channelId)
    {
        return new ProductBatchImportRequestedEvent
        {
            JobRunId = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            ChannelId = channelId,
            Cards = cards.ToList(),
            IsLastBatch = false,
        };
    }

    private static ChannelProductCard BuildCard(string externalProductId, string? offerId, params string[] barcodes)
    {
        return new ChannelProductCard
        {
            ExternalProductId = externalProductId,
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Card " + externalProductId,
            OfferId = offerId,
            Barcodes = barcodes.ToList(),
        };
    }

    private static Product BuildProduct(Guid productId, string code, string sku, string name)
    {
        return new Product
        {
            Id = productId,
            Code = code,
            Sku = sku,
            Name = name,
            Status = ProductStatus.Active,
            CreationSource = CreationSource.Manual,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
        };
    }

    private static ChannelProduct BuildLink(Guid productId, Guid channelId, string externalProductId)
    {
        return new ChannelProduct
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            ChannelId = channelId,
            ExternalProductId = externalProductId,
            ExternalSku = "ART-" + externalProductId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
        };
    }

    private static ProductBarcode BuildCanonicalBarcode(Guid productId, string value)
    {
        return new ProductBarcode
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            Value = value,
            Type = ChannelCodes.Wildberries,
            IsDefault = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
        };
    }

    private static ChannelProductBarcode BuildChannelBarcode(Guid channelProductId, string value)
    {
        return new ChannelProductBarcode
        {
            Id = Guid.NewGuid(),
            ChannelProductId = channelProductId,
            Value = value,
            Type = ChannelCodes.Wildberries,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
        };
    }

    private async Task<List<ChannelProductBarcode>> GetChannelBarcodesAsync(CatalogDbContext context)
    {
        return await context.ChannelProductBarcodes
            .IgnoreQueryFilters()
            .ToListAsync();
    }

    private async Task SeedChannelAsync(string databaseName, Guid? channelId = null, string name = "Wildberries")
    {
        using CatalogDbContext context = BuildContext(databaseName);

        await context.Channels.AddAsync(new Channel
        {
            Id = channelId ?? _channelId,
            Name = name,
            Code = name == "Wildberries" ? ChannelCodes.Wildberries : ChannelCodes.Ozon,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await context.SaveChangesAsync();
    }

    private async Task SeedProductWithCanonicalBarcodeAsync(
        string databaseName,
        string code,
        string sku,
        params string[] barcodes)
    {
        using CatalogDbContext context = BuildContext(databaseName);

        Product product = BuildProduct(Guid.NewGuid(), code, sku, "Seeded");

        foreach (string value in barcodes)
        {
            product.Barcodes.Add(BuildCanonicalBarcode(product.Id, value));
        }

        await context.Products.AddAsync(product);
        await context.SaveChangesAsync();
    }

    private async Task SeedLinkedProductAsync(
        string databaseName,
        string code,
        string sku,
        string externalProductId,
        params string[] channelBarcodes)
    {
        using CatalogDbContext context = BuildContext(databaseName);

        Product product = BuildProduct(Guid.NewGuid(), code, sku, "Seeded");
        ChannelProduct link = BuildLink(product.Id, _channelId, externalProductId);

        foreach (string value in channelBarcodes)
        {
            link.Barcodes.Add(BuildChannelBarcode(link.Id, value));
        }

        product.ChannelProducts.Add(link);

        await context.Products.AddAsync(product);
        await context.SaveChangesAsync();
    }

    private ProductBatchImporter BuildImporter(CatalogDbContext context, IPublishEndpoint publishEndpoint)
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
            new FakeNumberAllocator(),
            Mock.Of<ILogger<ProductBatchImporter>>(),
            TimeProvider.System);
    }

    private CatalogDbContext BuildContext(string databaseName)
    {
        DbContextOptions<CatalogDbContext> options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        Mock<ITenantContext> tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(context => context.TenantId).Returns(_tenantId);

        return new CatalogDbContext(options, tenantContextMock.Object);
    }
}
