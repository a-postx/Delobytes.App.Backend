using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Application.Products;
using Delobytes.App.Backend.Catalog.Application.Services;
using Delobytes.App.Backend.Catalog.Contracts.Events;
using Delobytes.App.Backend.Catalog.Domain;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Catalog.Infrastructure.Messaging;
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
using CardPhoto = Delobytes.App.Backend.Integrations.Contracts.Models.ChannelProductPhoto;
using ChannelPhotoRow = Delobytes.App.Backend.Catalog.Domain.Entities.ChannelProductPhoto;

namespace Delobytes.App.Backend.Tests.Infrastructure;

/// <summary>
/// The canonical gallery is written only when the product is created; every import refreshes the
/// gallery of its own channel. These tests run the real <see cref="ProductPhotoService"/> behind
/// the importer, so the split is exercised end to end rather than through a mock that could hide
/// a call to the wrong set.
/// </summary>
public class ProductPhotoSplitTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _channelId = Guid.NewGuid();
    private readonly Guid _connectionId = Guid.NewGuid();
    private readonly Guid _jobRunId = Guid.NewGuid();

    [Fact]
    public async Task ImportAsync_NewCard_CreatesCanonicalAndChannelPhotoSets()
    {
        string databaseName = Guid.NewGuid().ToString();
        await SeedChannelAsync(databaseName, _channelId, "wildberries");

        CatalogDbContext context = BuildContext(databaseName);
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductBatchImporter importer = BuildImporter(
            context,
            storage,
            (HttpStatusCode.OK, WebP(0x11)),
            (HttpStatusCode.OK, WebP(0x22)),
            (HttpStatusCode.OK, WebP(0x11)),
            (HttpStatusCode.OK, WebP(0x22)));

        ChannelProductCard card = BuildCard(
            "123456789",
            "SKU-001",
            "4600000000001",
            new CardPhoto { Url = "https://cdn.test/thumb.webp", SizeVariant = "thumbnail", DisplayOrder = 1 },
            new CardPhoto { Url = "https://cdn.test/large.webp", SizeVariant = "large", DisplayOrder = 1 });

        await importer.ImportAsync(BuildMessage(card, _channelId), CancellationToken.None);

        Product product = (await context.Products.IgnoreQueryFilters().ToListAsync()).Should().ContainSingle().Subject;
        List<ProductPhoto> canonical = await context.ProductPhotos.IgnoreQueryFilters().ToListAsync();
        List<ChannelPhotoRow> channel = await context.ChannelProductPhotos.IgnoreQueryFilters().ToListAsync();

        canonical.Should().HaveCount(2);
        canonical.Should().OnlyContain(p => p.Status == ProductPhotoStatus.Uploaded);
        channel.Should().HaveCount(2);
        channel.Should().OnlyContain(p => p.Status == ProductPhotoStatus.Uploaded);
        channel.Select(p => p.StorageKey).Should().BeEquivalentTo(canonical.Select(p => p.StorageKey),
            "identical bytes are content-addressed across both sets");
    }

    [Fact]
    public async Task ImportAsync_SecondImportOfSameCard_DoesNotTouchCanonicalPhotos()
    {
        string databaseName = Guid.NewGuid().ToString();
        await SeedChannelAsync(databaseName, _channelId, "wildberries");

        CatalogDbContext context = BuildContext(databaseName);
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductBatchImporter importer = BuildImporter(
            context,
            storage,
            (HttpStatusCode.OK, WebP(0x11)),
            (HttpStatusCode.OK, WebP(0x11)),
            (HttpStatusCode.OK, WebP(0x33)),
            (HttpStatusCode.OK, WebP(0x33)));

        ChannelProductCard firstCard = BuildCard(
            "123456789",
            "SKU-001",
            "4600000000001",
            new CardPhoto { Url = "https://cdn.test/first.webp", SizeVariant = "thumbnail", DisplayOrder = 1 });

        await importer.ImportAsync(BuildMessage(firstCard, _channelId), CancellationToken.None);

        List<ProductPhoto> canonicalBefore = await context.ProductPhotos.IgnoreQueryFilters().ToListAsync();
        canonicalBefore.Should().ContainSingle();

        ChannelProductCard secondCard = BuildCard(
            "123456789",
            "SKU-001",
            "4600000000001",
            new CardPhoto { Url = "https://cdn.test/second.webp", SizeVariant = "thumbnail", DisplayOrder = 1 });

        await importer.ImportAsync(BuildMessage(secondCard, _channelId), CancellationToken.None);

        List<ProductPhoto> canonicalAfter = await context.ProductPhotos.IgnoreQueryFilters().ToListAsync();

        canonicalAfter.Should().ContainSingle("a re-import never adds or replaces a canonical photo");
        canonicalAfter[0].Id.Should().Be(canonicalBefore[0].Id);
        canonicalAfter[0].ContentHash.Should().Be(canonicalBefore[0].ContentHash);
        canonicalAfter[0].Status.Should().Be(ProductPhotoStatus.Uploaded);

        (await context.ChannelProductPhotos.IgnoreQueryFilters().ToListAsync())
            .Should().ContainSingle(p => p.Status == ProductPhotoStatus.Uploaded)
            .Which.ContentHash.Should().Be(Sha256Hex(WebP(0x33)));
    }

    [Fact]
    public async Task ImportAsync_SecondChannel_LinksProduct_AddsNoCanonicalPhotos()
    {
        // The regression case behind the defect: the first import created the product and its
        // gallery; the second channel must only add photos to its own card.
        string databaseName = Guid.NewGuid().ToString();
        Guid ozonChannelId = Guid.NewGuid();

        await SeedChannelAsync(databaseName, _channelId, "wildberries");
        await SeedChannelAsync(databaseName, ozonChannelId, "ozon");

        CatalogDbContext context = BuildContext(databaseName);
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductBatchImporter importer = BuildImporter(
            context,
            storage,
            (HttpStatusCode.OK, WebP(0x11)),
            (HttpStatusCode.OK, WebP(0x11)),
            (HttpStatusCode.OK, WebP(0x44)),
            (HttpStatusCode.OK, WebP(0x44)));

        ChannelProductCard wildberriesCard = BuildCard(
            "111000111",
            "ART-WB",
            "4600000000009",
            new CardPhoto { Url = "https://cdn.test/wb.webp", SizeVariant = "thumbnail", DisplayOrder = 1 });

        await importer.ImportAsync(BuildMessage(wildberriesCard, _channelId), CancellationToken.None);

        ChannelProductCard ozonCard = BuildOzonCard(
            "222000222",
            "ART-OZON",
            "4600000000009",
            new CardPhoto { Url = "https://cdn.test/ozon.webp", SizeVariant = "thumbnail", DisplayOrder = 1 });

        await importer.ImportAsync(BuildMessage(ozonCard, ozonChannelId), CancellationToken.None);

        Product product = (await context.Products.IgnoreQueryFilters().ToListAsync()).Should().ContainSingle().Subject;
        List<ChannelProduct> links = await context.ChannelProducts.IgnoreQueryFilters().ToListAsync();

        links.Should().HaveCount(2);
        (await context.ProductPhotos.IgnoreQueryFilters().ToListAsync())
            .Should().ContainSingle("only the creating import writes the canonical gallery");

        List<ChannelPhotoRow> channelPhotos = await context.ChannelProductPhotos.IgnoreQueryFilters().ToListAsync();
        channelPhotos.Should().HaveCount(2);

        ChannelProduct wildberriesLink = links.Single(l => l.ChannelId == _channelId);
        ChannelProduct ozonLink = links.Single(l => l.ChannelId == ozonChannelId);

        channelPhotos.Should().ContainSingle(p => p.ChannelProductId == wildberriesLink.Id);
        channelPhotos.Should().ContainSingle(p => p.ChannelProductId == ozonLink.Id);
    }

    [Fact]
    public async Task ImportAsync_ChannelDropsAPhoto_RemovesOnlyThatChannelRow()
    {
        string databaseName = Guid.NewGuid().ToString();
        await SeedChannelAsync(databaseName, _channelId, "wildberries");

        CatalogDbContext context = BuildContext(databaseName);
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductBatchImporter importer = BuildImporter(
            context,
            storage,
            (HttpStatusCode.OK, WebP(0x11)),
            (HttpStatusCode.OK, WebP(0x55)),
            (HttpStatusCode.OK, WebP(0x11)),
            (HttpStatusCode.OK, WebP(0x55)),
            (HttpStatusCode.OK, WebP(0x11)),
            (HttpStatusCode.OK, WebP(0x11)));

        ChannelProductCard firstCard = BuildCard(
            "123456789",
            "SKU-001",
            "4600000000001",
            new CardPhoto { Url = "https://cdn.test/kept.webp", SizeVariant = "thumbnail", DisplayOrder = 1 },
            new CardPhoto { Url = "https://cdn.test/dropped.webp", SizeVariant = "thumbnail", DisplayOrder = 2 });

        await importer.ImportAsync(BuildMessage(firstCard, _channelId), CancellationToken.None);

        ChannelProductCard secondCard = BuildCard(
            "123456789",
            "SKU-001",
            "4600000000001",
            new CardPhoto { Url = "https://cdn.test/kept.webp", SizeVariant = "thumbnail", DisplayOrder = 1 });

        await importer.ImportAsync(BuildMessage(secondCard, _channelId), CancellationToken.None);

        List<ChannelPhotoRow> channelPhotos = await context.ChannelProductPhotos
            .IgnoreQueryFilters()
            .ToListAsync();

        channelPhotos.Should().HaveCount(2, "the superseded row is retained during the grace period");
        channelPhotos.Should().ContainSingle(p => p.Status == ProductPhotoStatus.Removed)
            .Which.ExternalId.Should().Be("123456789_2_thumbnail");

        (await context.ProductPhotos.IgnoreQueryFilters().ToListAsync())
            .Should().HaveCount(2)
            .And.OnlyContain(p => p.Status == ProductPhotoStatus.Uploaded,
                "the canonical gallery is untouched by a channel's dropped photo");
    }

    [Fact]
    public async Task ImportAsync_NewCardWithFailedPhotoDownload_LeavesCanonicalGalleryEmpty()
    {
        // Accepted risk of the split: when the creating import cannot download the photos, the
        // canonical gallery stays without an uploaded photo. Pinned so a later change is a
        // conscious decision rather than an accident.
        string databaseName = Guid.NewGuid().ToString();
        await SeedChannelAsync(databaseName, _channelId, "wildberries");

        CatalogDbContext context = BuildContext(databaseName);
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductBatchImporter importer = BuildImporter(
            context,
            storage,
            (HttpStatusCode.NotFound, Array.Empty<byte>()),
            (HttpStatusCode.NotFound, Array.Empty<byte>()));

        ChannelProductCard card = BuildCard(
            "123456789",
            "SKU-001",
            "4600000000001",
            new CardPhoto { Url = "https://cdn.test/missing.webp", SizeVariant = "thumbnail", DisplayOrder = 1 });

        await importer.ImportAsync(BuildMessage(card, _channelId), CancellationToken.None);

        (await context.Products.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await context.ProductPhotos.IgnoreQueryFilters().ToListAsync())
            .Should().NotContain(p => p.Status == ProductPhotoStatus.Uploaded);
    }

    [Fact]
    public async Task PublishChannelProductLinks_UsesTheCardsOwnPrimaryPhoto()
    {
        // The linked event drives the Sales-side projection. For an Ozon card it must carry the
        // Ozon thumbnail, not the photo the Wildberries import put in the canonical gallery.
        string databaseName = Guid.NewGuid().ToString();
        Guid ozonChannelId = Guid.NewGuid();

        await SeedChannelAsync(databaseName, _channelId, "wildberries");
        await SeedChannelAsync(databaseName, ozonChannelId, "ozon");

        CatalogDbContext context = BuildContext(databaseName);
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        storage
            .Setup(s => s.GetPublicUrl(It.IsAny<StorageArea>(), It.IsAny<string>()))
            .Returns<StorageArea, string>((_, key) => "https://cdn.test/" + key);

        List<ChannelProductLinkedEvent> linkedEvents = new List<ChannelProductLinkedEvent>();
        Mock<IPublishEndpoint> publishEndpoint = new Mock<IPublishEndpoint>();
        publishEndpoint
            .Setup(p => p.Publish(It.IsAny<ChannelProductLinkedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ChannelProductLinkedEvent, CancellationToken>((e, _) => linkedEvents.Add(e))
            .Returns(Task.CompletedTask);

        ProductBatchImporter importer = BuildImporter(
            context,
            storage,
            publishEndpoint.Object,
            (HttpStatusCode.OK, WebP(0x11)),
            (HttpStatusCode.OK, WebP(0x11)),
            (HttpStatusCode.OK, WebP(0x66)),
            (HttpStatusCode.OK, WebP(0x66)));

        ChannelProductCard wildberriesCard = BuildCard(
            "111000111",
            "ART-WB",
            "4600000000009",
            new CardPhoto { Url = "https://cdn.test/wb.webp", SizeVariant = "thumbnail", DisplayOrder = 1 });

        await importer.ImportAsync(BuildMessage(wildberriesCard, _channelId), CancellationToken.None);

        ChannelProductCard ozonCard = BuildOzonCard(
            "222000222",
            "ART-OZON",
            "4600000000009",
            new CardPhoto { Url = "https://cdn.test/ozon.webp", SizeVariant = "thumbnail", DisplayOrder = 1 });

        await importer.ImportAsync(BuildMessage(ozonCard, ozonChannelId), CancellationToken.None);

        Product product = (await context.Products.IgnoreQueryFilters().ToListAsync()).Should().ContainSingle().Subject;
        string expectedOzonUrl = "https://cdn.test/"
            + ProductPhotoKeys.Build(_tenantId, product.Id, Sha256Hex(WebP(0x66)));
        string expectedWildberriesUrl = "https://cdn.test/"
            + ProductPhotoKeys.Build(_tenantId, product.Id, Sha256Hex(WebP(0x11)));

        ChannelProductLinkedEvent ozonEvent = linkedEvents.Should()
            .ContainSingle(e => e.ChannelId == ozonChannelId).Subject;
        ozonEvent.PhotoUrl.Should().Be(expectedOzonUrl);
        ozonEvent.PhotoUrl.Should().NotBe(expectedWildberriesUrl);

        linkedEvents.Should().ContainSingle(e => e.ChannelId == _channelId)
            .Which.PhotoUrl.Should().Be(expectedWildberriesUrl);
    }

    private static string Sha256Hex(byte[] payload)
    {
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }

    private static byte[] WebP(byte filler)
    {
        byte[] payload = new byte[64];
        payload[0] = (byte)'R';
        payload[1] = (byte)'I';
        payload[2] = (byte)'F';
        payload[3] = (byte)'F';
        payload[8] = (byte)'W';
        payload[9] = (byte)'E';
        payload[10] = (byte)'B';
        payload[11] = (byte)'P';
        payload[20] = filler;

        return payload;
    }

    private static ChannelProductCard BuildCard(
        string externalProductId,
        string offerId,
        string barcode,
        params CardPhoto[] photos)
    {
        return new ChannelProductCard
        {
            ExternalProductId = externalProductId,
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Card " + externalProductId,
            OfferId = offerId,
            Barcodes = new List<string> { barcode },
            Photos = photos.ToList(),
        };
    }

    private static ChannelProductCard BuildOzonCard(
        string externalProductId,
        string offerId,
        string barcode,
        params CardPhoto[] photos)
    {
        return new ChannelProductCard
        {
            ExternalProductId = externalProductId,
            ChannelCode = ChannelCodes.Ozon,
            BarcodeType = ChannelCodes.Ozon,
            Name = "Card " + externalProductId,
            OfferId = offerId,
            Barcodes = new List<string> { barcode },
            Photos = photos.ToList(),
        };
    }

    private ProductBatchImporter BuildImporter(
        CatalogDbContext context,
        Mock<IObjectStorage> storage,
        params (HttpStatusCode Status, byte[] Body)[] responses)
    {
        return BuildImporter(context, storage, new Mock<IPublishEndpoint>().Object, responses);
    }

    private ProductBatchImporter BuildImporter(
        CatalogDbContext context,
        Mock<IObjectStorage> storage,
        IPublishEndpoint publishEndpoint,
        params (HttpStatusCode Status, byte[] Body)[] responses)
    {
        Mock<IHttpClientFactory> httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(f => f.CreateClient(ProductPhotoService.HttpClientName))
            .Returns(new HttpClient(new QueueHttpMessageHandler(responses)));

        Mock<ITenantContext> tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.TenantId).Returns(_tenantId);

        ProductPhotoService photoService = new ProductPhotoService(
            storage.Object,
            httpClientFactory.Object,
            tenantContext.Object,
            Mock.Of<ILogger<ProductPhotoService>>(),
            TimeProvider.System);

        return new ProductBatchImporter(
            new ProductImportStore(context),
            photoService,
            new MassTransitEventPublisher(publishEndpoint),
            new FakeNumberingSchemeProvider(),
            new FakeNumberAllocator(),
            Mock.Of<ILogger<ProductBatchImporter>>(),
            TimeProvider.System);
    }

    private ProductBatchImportRequestedEvent BuildMessage(ChannelProductCard card, Guid channelId)
    {
        return new ProductBatchImportRequestedEvent
        {
            JobRunId = _jobRunId,
            ConnectionId = _connectionId,
            ChannelId = channelId,
            Cards = new List<ChannelProductCard> { card },
            IsLastBatch = true,
        };
    }

    private async Task SeedChannelAsync(string databaseName, Guid channelId, string code)
    {
        using CatalogDbContext context = BuildContext(databaseName);

        await context.Channels.AddAsync(new Channel
        {
            Id = channelId,
            Name = code,
            Code = code,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await context.SaveChangesAsync();
    }

    private CatalogDbContext BuildContext(string databaseName)
    {
        DbContextOptions<CatalogDbContext> options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        Mock<ITenantContext> tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.TenantId).Returns(_tenantId);

        return new CatalogDbContext(options, tenantContext.Object);
    }

    private sealed class QueueHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, byte[] Body)> _responses;

        public QueueHttpMessageHandler(IEnumerable<(HttpStatusCode Status, byte[] Body)> responses)
        {
            _responses = new Queue<(HttpStatusCode Status, byte[] Body)>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No stubbed photo response is queued.");
            }

            (HttpStatusCode status, byte[] body) = _responses.Dequeue();

            HttpResponseMessage response = new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent(body),
            };

            return Task.FromResult(response);
        }
    }
}
