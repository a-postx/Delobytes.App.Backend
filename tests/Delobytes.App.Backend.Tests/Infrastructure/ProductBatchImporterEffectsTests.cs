using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Application.Products;
using Delobytes.App.Backend.Catalog.Contracts.Events;
using Delobytes.App.Backend.Catalog.Domain.Entities;
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
using CardPhoto = Delobytes.App.Backend.Integrations.Contracts.Models.ChannelProductPhoto;

namespace Delobytes.App.Backend.Tests.Infrastructure;

/// <summary>
/// A card that fails to save must not reach the marketplace: its photos are not downloaded and its
/// link is not announced to Sales. A batch that fails to commit must not report a batch result,
/// because MassTransit retries it and the retry publishes its own.
/// </summary>
public class ProductBatchImporterEffectsTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _channelId = Guid.NewGuid();
    private readonly Guid _connectionId = Guid.NewGuid();
    private readonly Guid _jobRunId = Guid.NewGuid();
    private readonly string _databaseName = Guid.NewGuid().ToString();

    [Fact]
    public async Task FailedCard_ProducesNeitherPhotosNorALinkEvent()
    {
        CatalogDbContext setupContext = BuildContext();
        await CreateChannelAsync(setupContext);

        CatalogDbContext consumerContext = BuildContext();
        Mock<IProductPhotoService> photoServiceMock = BuildPhotoServiceMock();
        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();

        ProductBatchImporter importer = BuildImporter(
            new FailingProductImportStore(new ProductImportStore(consumerContext), failCardSave: true),
            photoServiceMock.Object,
            publishEndpointMock.Object);

        ChannelProductCard card = BuildCard("123456789", "SKU-1", "4600000000001");

        await importer.ImportAsync(BuildMessage(card), CancellationToken.None);

        photoServiceMock.Verify(
            service => service.ImportCanonicalPhotosAsync(
                It.IsAny<Product>(),
                It.IsAny<IReadOnlyList<MarketplacePhotoSource>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        photoServiceMock.Verify(
            service => service.SyncChannelPhotosAsync(
                It.IsAny<ChannelProduct>(),
                It.IsAny<IReadOnlyList<MarketplacePhotoSource>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        publishEndpointMock.Verify(
            endpoint => endpoint.Publish(
                It.IsAny<ChannelProductLinkedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        publishEndpointMock.Verify(
            endpoint => endpoint.Publish(
                It.Is<ProductBatchImportCompletedEvent>(e => e.RecordsFailed == 1 && e.RecordsCreated == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FailedCommit_ReportsNoBatchResult()
    {
        CatalogDbContext setupContext = BuildContext();
        await CreateChannelAsync(setupContext);

        CatalogDbContext consumerContext = BuildContext();
        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();

        ProductBatchImporter importer = BuildImporter(
            new FailingProductImportStore(new ProductImportStore(consumerContext), failCommit: true),
            BuildPhotoServiceMock().Object,
            publishEndpointMock.Object);

        Func<Task> act = () => importer.ImportAsync(
            BuildMessage(BuildCard("123456789", "SKU-1", "4600000000001")),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();

        publishEndpointMock.Verify(
            endpoint => endpoint.Publish(
                It.IsAny<ProductBatchImportCompletedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private ProductBatchImporter BuildImporter(
        IProductImportStore store,
        IProductPhotoService photoService,
        IPublishEndpoint publishEndpoint)
    {
        return ProductImportComposition.Build(store, photoService, publishEndpoint);
    }

    private static ChannelProductCard BuildCard(string externalProductId, string offerId, string barcode)
    {
        return new ChannelProductCard
        {
            ExternalProductId = externalProductId,
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = "Imported product",
            OfferId = offerId,
            Barcodes = new List<string> { barcode },
            Photos = new List<CardPhoto>
            {
                new CardPhoto
                {
                    Url = "https://cdn.example.com/photo.webp",
                    SizeVariant = "thumbnail",
                    DisplayOrder = 1,
                },
            },
        };
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

    private static Mock<IProductPhotoService> BuildPhotoServiceMock()
    {
        Mock<IProductPhotoService> photoServiceMock = new Mock<IProductPhotoService>();

        photoServiceMock
            .Setup(service => service.ImportCanonicalPhotosAsync(
                It.IsAny<Product>(),
                It.IsAny<IReadOnlyList<MarketplacePhotoSource>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductPhotoImportResult());

        photoServiceMock
            .Setup(service => service.SyncChannelPhotosAsync(
                It.IsAny<ChannelProduct>(),
                It.IsAny<IReadOnlyList<MarketplacePhotoSource>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductPhotoImportResult());

        return photoServiceMock;
    }

    private async Task CreateChannelAsync(CatalogDbContext context)
    {
        await context.Channels.AddAsync(new Channel
        {
            Id = _channelId,
            Name = "Wildberries",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await context.SaveChangesAsync();
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
