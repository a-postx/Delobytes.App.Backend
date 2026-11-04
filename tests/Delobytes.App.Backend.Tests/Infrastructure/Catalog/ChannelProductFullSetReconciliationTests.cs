using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Interfaces;
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

namespace Delobytes.App.Backend.Tests.Infrastructure.Catalog;

/// <summary>
/// Covers the full-set reconciliation of <see cref="ChannelProduct.IsActive"/>: on the terminal
/// batch of a complete listing import, links the run never observed are deactivated, and every
/// condition that makes the run an unreliable witness of the listing suppresses the sweep.
///
/// The consumer processes one batch and cannot see its siblings, so "seen by this run" is decided
/// by comparing <see cref="ChannelProduct.LastSyncedAt"/> against the run's start instant. These
/// tests pin that boundary and each of its guards.
/// </summary>
public class ChannelProductFullSetReconciliationTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _channelId = Guid.NewGuid();
    private readonly Guid _otherChannelId = Guid.NewGuid();
    private readonly Guid _connectionId = Guid.NewGuid();
    private readonly Guid _jobRunId = Guid.NewGuid();

    [Fact]
    public async Task ProcessAsync_TerminalBatch_DeactivatesLinksMissingFromListing()
    {
        string databaseName = Guid.NewGuid().ToString();
        DateTimeOffset runStartedAt = DateTimeOffset.UtcNow;

        CatalogDbContext setupContext = BuildCatalogDbContext(databaseName);
        await CreateChannelAsync(setupContext, _channelId);
        await CreateChannelAsync(setupContext, _otherChannelId);

        // 111 is in the batch below, 999 was listed by an earlier import only.
        await SeedLinkAsync(setupContext, _channelId, "111", runStartedAt.AddDays(-1));
        await SeedLinkAsync(setupContext, _channelId, "999", runStartedAt.AddDays(-1));

        CatalogDbContext consumerContext = BuildCatalogDbContext(databaseName);
        ImportProductBatchConsumer consumer = BuildConsumer(consumerContext);

        await consumer.ProcessAsync(
            BuildMessage(
                new List<WildberriesCardSnapshot> { BuildCard(111, "SKU-111") },
                isLastBatch: true,
                runStartedAt: runStartedAt),
            CancellationToken.None);

        (await IsLinkActiveAsync(consumerContext, _channelId, "111")).Should().BeTrue(
            "the run observed this card, so the link is live");
        (await IsLinkActiveAsync(consumerContext, _channelId, "999")).Should().BeFalse(
            "the card was absent from the full listing this run walked");
    }

    [Fact]
    public async Task ProcessAsync_NonTerminalBatch_LeavesLinksAlone()
    {
        string databaseName = Guid.NewGuid().ToString();
        DateTimeOffset runStartedAt = DateTimeOffset.UtcNow;

        CatalogDbContext setupContext = BuildCatalogDbContext(databaseName);
        await CreateChannelAsync(setupContext, _channelId);
        await SeedLinkAsync(setupContext, _channelId, "999", runStartedAt.AddDays(-1));

        CatalogDbContext consumerContext = BuildCatalogDbContext(databaseName);
        ImportProductBatchConsumer consumer = BuildConsumer(consumerContext);

        await consumer.ProcessAsync(
            BuildMessage(
                new List<WildberriesCardSnapshot> { BuildCard(111, "SKU-111") },
                isLastBatch: false,
                runStartedAt: runStartedAt),
            CancellationToken.None);

        (await IsLinkActiveAsync(consumerContext, _channelId, "999")).Should().BeTrue(
            "a non-terminal batch says nothing about the cards later batches will carry");
    }

    [Fact]
    public async Task ProcessAsync_PartialRun_LeavesLinksAlone()
    {
        string databaseName = Guid.NewGuid().ToString();
        DateTimeOffset runStartedAt = DateTimeOffset.UtcNow;

        CatalogDbContext setupContext = BuildCatalogDbContext(databaseName);
        await CreateChannelAsync(setupContext, _channelId);
        await SeedLinkAsync(setupContext, _channelId, "999", runStartedAt.AddDays(-1));

        CatalogDbContext consumerContext = BuildCatalogDbContext(databaseName);
        ImportProductBatchConsumer consumer = BuildConsumer(consumerContext);

        // A cancelled job publishes a terminal batch for a listing it never finished walking.
        await consumer.ProcessAsync(
            BuildMessage(
                new List<WildberriesCardSnapshot> { BuildCard(111, "SKU-111") },
                isLastBatch: true,
                runStartedAt: runStartedAt,
                isPartial: true),
            CancellationToken.None);

        (await IsLinkActiveAsync(consumerContext, _channelId, "999")).Should().BeTrue(
            "the cards a cancelled run never fetched were not absent from the channel");
    }

    [Fact]
    public async Task ProcessAsync_MissingRunStart_LeavesLinksAlone()
    {
        string databaseName = Guid.NewGuid().ToString();

        CatalogDbContext setupContext = BuildCatalogDbContext(databaseName);
        await CreateChannelAsync(setupContext, _channelId);
        await SeedLinkAsync(setupContext, _channelId, "999", DateTimeOffset.UtcNow.AddDays(-30));

        CatalogDbContext consumerContext = BuildCatalogDbContext(databaseName);
        ImportProductBatchConsumer consumer = BuildConsumer(consumerContext);

        await consumer.ProcessAsync(
            BuildMessage(
                new List<WildberriesCardSnapshot> { BuildCard(111, "SKU-111") },
                isLastBatch: true,
                runStartedAt: null),
            CancellationToken.None);

        (await IsLinkActiveAsync(consumerContext, _channelId, "999")).Should().BeTrue(
            "without a boundary every link would look abandoned, so the sweep must not run at all");
    }

    [Fact]
    public async Task ProcessAsync_BatchWithFailures_LeavesLinksAlone()
    {
        string databaseName = Guid.NewGuid().ToString();
        DateTimeOffset runStartedAt = DateTimeOffset.UtcNow;

        CatalogDbContext setupContext = BuildCatalogDbContext(databaseName);
        await CreateChannelAsync(setupContext, _channelId);
        await SeedLinkAsync(setupContext, _channelId, "999", runStartedAt.AddDays(-1));

        // A product already owns this SKU, so the card below cannot be imported: the batch reports
        // one failure and its own stamps may have been rolled back to the card savepoint.
        await setupContext.Products.AddAsync(new Product
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-TAKEN",
            Name = "Existing product",
            Status = ProductStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
        });

        await setupContext.SaveChangesAsync();

        CatalogDbContext consumerContext = BuildCatalogDbContext(databaseName);
        ImportProductBatchConsumer consumer = BuildConsumer(consumerContext);

        await consumer.ProcessAsync(
            BuildMessage(
                new List<WildberriesCardSnapshot> { BuildCard(111, "SKU-TAKEN") },
                isLastBatch: true,
                runStartedAt: runStartedAt),
            CancellationToken.None);

        (await IsLinkActiveAsync(consumerContext, _channelId, "999")).Should().BeTrue(
            "a card this batch could not import is indistinguishable from a dropped card");
    }

    [Fact]
    public async Task ProcessAsync_NeverSyncedLink_IsNotDeactivated()
    {
        string databaseName = Guid.NewGuid().ToString();
        DateTimeOffset runStartedAt = DateTimeOffset.UtcNow;

        CatalogDbContext setupContext = BuildCatalogDbContext(databaseName);
        await CreateChannelAsync(setupContext, _channelId);
        await SeedLinkAsync(setupContext, _channelId, "999", lastSyncedAt: null);

        CatalogDbContext consumerContext = BuildCatalogDbContext(databaseName);
        ImportProductBatchConsumer consumer = BuildConsumer(consumerContext);

        await consumer.ProcessAsync(
            BuildMessage(
                new List<WildberriesCardSnapshot> { BuildCard(111, "SKU-111") },
                isLastBatch: true,
                runStartedAt: runStartedAt),
            CancellationToken.None);

        (await IsLinkActiveAsync(consumerContext, _channelId, "999")).Should().BeTrue(
            "a link created outside any import carries no stamp, so no run can claim to have missed it");
    }

    [Fact]
    public async Task ProcessAsync_OtherChannelLinks_AreUntouched()
    {
        string databaseName = Guid.NewGuid().ToString();
        DateTimeOffset runStartedAt = DateTimeOffset.UtcNow;

        CatalogDbContext setupContext = BuildCatalogDbContext(databaseName);
        await CreateChannelAsync(setupContext, _channelId);
        await CreateChannelAsync(setupContext, _otherChannelId);
        await SeedLinkAsync(setupContext, _otherChannelId, "999", runStartedAt.AddDays(-1));

        CatalogDbContext consumerContext = BuildCatalogDbContext(databaseName);
        ImportProductBatchConsumer consumer = BuildConsumer(consumerContext);

        await consumer.ProcessAsync(
            BuildMessage(
                new List<WildberriesCardSnapshot> { BuildCard(111, "SKU-111") },
                isLastBatch: true,
                runStartedAt: runStartedAt),
            CancellationToken.None);

        (await IsLinkActiveAsync(consumerContext, _otherChannelId, "999")).Should().BeTrue(
            "a listing import describes one channel and cannot speak for another");
    }

    [Fact]
    public async Task ProcessAsync_CardReturnsAfterDeactivation_ReactivatesLink()
    {
        string databaseName = Guid.NewGuid().ToString();
        DateTimeOffset runStartedAt = DateTimeOffset.UtcNow;

        CatalogDbContext setupContext = BuildCatalogDbContext(databaseName);
        await CreateChannelAsync(setupContext, _channelId);

        Guid linkId = await SeedLinkAsync(setupContext, _channelId, "111", runStartedAt.AddDays(-5), isActive: false);

        CatalogDbContext consumerContext = BuildCatalogDbContext(databaseName);
        ImportProductBatchConsumer consumer = BuildConsumer(consumerContext);

        await consumer.ProcessAsync(
            BuildMessage(
                new List<WildberriesCardSnapshot> { BuildCard(111, "SKU-111") },
                isLastBatch: true,
                runStartedAt: runStartedAt),
            CancellationToken.None);

        ChannelProduct? link = await consumerContext.ChannelProducts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(cp => cp.Id == linkId);

        link.Should().NotBeNull();
        link!.IsActive.Should().BeTrue(
            "the card is listed again, so the link the sweep deactivated is live again");
        link.LastSyncedAt.Should().BeOnOrAfter(runStartedAt);
    }

    private ProductBatchImportRequestedEvent BuildMessage(
        List<WildberriesCardSnapshot> cards,
        bool isLastBatch,
        DateTimeOffset? runStartedAt,
        bool isPartial = false)
    {
        return new ProductBatchImportRequestedEvent
        {
            JobRunId = _jobRunId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            Cards = cards,
            IsLastBatch = isLastBatch,
            IsPartial = isPartial,
            RunStartedAt = runStartedAt,
            TotalBatches = 1,
        };
    }

    private static WildberriesCardSnapshot BuildCard(long nmId, string vendorCode)
    {
        return new WildberriesCardSnapshot
        {
            NmId = nmId,
            Name = "Product " + nmId,
            VendorCode = vendorCode,
            Barcodes = new List<string>(),
            Description = "Description",
        };
    }

    private static async Task<bool> IsLinkActiveAsync(
        CatalogDbContext context, Guid channelId, string externalProductId)
    {
        ChannelProduct? link = await context.ChannelProducts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(cp => cp.ChannelId == channelId && cp.ExternalProductId == externalProductId);

        link.Should().NotBeNull();
        return link!.IsActive;
    }

    /// <summary>
    /// Seeds a link that belongs to a previous import. Its <paramref name="lastSyncedAt"/> is what
    /// the sweep reads, so the tests set it explicitly instead of letting the import stamp it.
    /// </summary>
    private async Task<Guid> SeedLinkAsync(
        CatalogDbContext context,
        Guid channelId,
        string externalProductId,
        DateTimeOffset? lastSyncedAt,
        bool isActive = true)
    {
        Product product = new Product
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-" + Guid.NewGuid().ToString("N")[..8],
            Name = "Product " + externalProductId,
            Status = ProductStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
        };

        await context.Products.AddAsync(product);

        ChannelProduct link = new ChannelProduct
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ChannelId = channelId,
            ExternalProductId = externalProductId,
            ExternalSku = product.Sku,
            IsActive = isActive,
            LastSyncedAt = lastSyncedAt,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
        };

        await context.ChannelProducts.AddAsync(link);
        await context.SaveChangesAsync();

        return link.Id;
    }

    private async Task CreateChannelAsync(CatalogDbContext context, Guid channelId)
    {
        await context.Channels.AddAsync(new Channel
        {
            Id = channelId,
            Name = "Channel " + channelId.ToString("N")[..6],
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await context.SaveChangesAsync();
    }

    private CatalogDbContext BuildCatalogDbContext(string databaseName)
    {
        DbContextOptions<CatalogDbContext> options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(databaseName: databaseName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        Mock<ITenantContext> tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(tc => tc.TenantId).Returns(_tenantId);

        return new CatalogDbContext(options, tenantContextMock.Object);
    }

    private static ImportProductBatchConsumer BuildConsumer(CatalogDbContext context)
    {
        Mock<IProductPhotoService> photoServiceMock = new Mock<IProductPhotoService>();
        photoServiceMock
            .Setup(s => s.ImportPhotosAsync(
                It.IsAny<Product>(),
                It.IsAny<IReadOnlyList<MarketplacePhotoSource>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductPhotoImportResult());

        return new ImportProductBatchConsumer(
            context,
            new Mock<IPublishEndpoint>().Object,
            photoServiceMock.Object,
            Mock.Of<ILogger<ImportProductBatchConsumer>>());
    }
}
