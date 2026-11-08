using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Catalog.Infrastructure.Messaging.Consumers;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure.Catalog;

/// <summary>
/// Covers <see cref="ChannelProductFullSetReconciliationConsumer"/>: the sweep that marks a
/// channel's links inactive when the finished import run never observed them.
///
/// These tests are deliberately written against the consumer directly rather than through the batch
/// import. The bug this design replaced lived precisely in that coupling: the sweep used to run
/// inside a batch transaction, where its siblings' committed-but-not-yet-visible rows looked like
/// dropped cards. Exercising the sweep in isolation pins the contract it actually has — it is given
/// a run boundary by Integrations once every batch is recorded, and asks nothing of the batches.
/// </summary>
public class ChannelProductFullSetReconciliationTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _channelId = Guid.NewGuid();
    private readonly Guid _otherChannelId = Guid.NewGuid();
    private readonly Guid _connectionId = Guid.NewGuid();
    private readonly Guid _jobRunId = Guid.NewGuid();

    [Fact]
    public async Task ProcessAsync_LinksNotTouchedByRun_AreDeactivated()
    {
        string databaseName = Guid.NewGuid().ToString();
        DateTimeOffset runStartedAt = DateTimeOffset.UtcNow;

        using CatalogDbContext context = BuildCatalogDbContext(databaseName);
        await CreateChannelAsync(context, _channelId);

        // Seen by an earlier run, absent from this one.
        await SeedLinkAsync(context, _channelId, "999", runStartedAt.AddDays(-1));

        // Seen by this run: stamped at or after its start.
        Guid liveLinkId = await SeedLinkAsync(context, _channelId, "111", runStartedAt.AddSeconds(5));

        ChannelProductFullSetReconciliationConsumer consumer = BuildConsumer(context);

        int deactivated = await consumer.ProcessAsync(
            BuildMessage(runStartedAt),
            CancellationToken.None);

        deactivated.Should().Be(1);
        (await IsActiveAsync(context, liveLinkId)).Should().BeTrue(
            "this card was imported by the run, so its link is live");
        (await IsLinkActiveAsync(context, _channelId, "999")).Should().BeFalse(
            "the card was absent from the listing the run walked");
    }

    [Fact]
    public async Task ProcessAsync_LinkStampedExactlyAtRunStart_StaysActive()
    {
        string databaseName = Guid.NewGuid().ToString();
        DateTimeOffset runStartedAt = DateTimeOffset.UtcNow;

        using CatalogDbContext context = BuildCatalogDbContext(databaseName);
        await CreateChannelAsync(context, _channelId);

        // The comparison is strict: a stamp equal to the boundary belongs to this run, and treating
        // it as older would deactivate cards imported in the first milliseconds of every run.
        await SeedLinkAsync(context, _channelId, "111", runStartedAt);

        ChannelProductFullSetReconciliationConsumer consumer = BuildConsumer(context);

        int deactivated = await consumer.ProcessAsync(BuildMessage(runStartedAt), CancellationToken.None);

        deactivated.Should().Be(0);
        (await IsLinkActiveAsync(context, _channelId, "111")).Should().BeTrue();
    }

    [Fact]
    public async Task ProcessAsync_AlreadyInactiveLink_IsNotCountedAgain()
    {
        string databaseName = Guid.NewGuid().ToString();
        DateTimeOffset runStartedAt = DateTimeOffset.UtcNow;

        using CatalogDbContext context = BuildCatalogDbContext(databaseName);
        await CreateChannelAsync(context, _channelId);
        await SeedLinkAsync(context, _channelId, "999", runStartedAt.AddDays(-1), isActive: false);

        ChannelProductFullSetReconciliationConsumer consumer = BuildConsumer(context);

        int deactivated = await consumer.ProcessAsync(BuildMessage(runStartedAt), CancellationToken.None);

        // Idempotency matters: a redelivered request must not report work it did not do, and must
        // not resurrect or re-touch rows a previous delivery already handled.
        deactivated.Should().Be(0);
        (await IsLinkActiveAsync(context, _channelId, "999")).Should().BeFalse();
    }

    [Fact]
    public async Task ProcessAsync_NeverSyncedLink_IsNotDeactivated()
    {
        string databaseName = Guid.NewGuid().ToString();
        DateTimeOffset runStartedAt = DateTimeOffset.UtcNow;

        using CatalogDbContext context = BuildCatalogDbContext(databaseName);
        await CreateChannelAsync(context, _channelId);

        // Created outside any import: no stamp, so no run can claim to have observed its absence.
        await SeedLinkAsync(context, _channelId, "999", lastSyncedAt: null);

        ChannelProductFullSetReconciliationConsumer consumer = BuildConsumer(context);

        int deactivated = await consumer.ProcessAsync(BuildMessage(runStartedAt), CancellationToken.None);

        deactivated.Should().Be(0);
        (await IsLinkActiveAsync(context, _channelId, "999")).Should().BeTrue();
    }

    [Fact]
    public async Task ProcessAsync_OtherChannelLinks_AreUntouched()
    {
        string databaseName = Guid.NewGuid().ToString();
        DateTimeOffset runStartedAt = DateTimeOffset.UtcNow;

        using CatalogDbContext context = BuildCatalogDbContext(databaseName);
        await CreateChannelAsync(context, _channelId);
        await CreateChannelAsync(context, _otherChannelId);
        await SeedLinkAsync(context, _otherChannelId, "999", runStartedAt.AddDays(-1));

        ChannelProductFullSetReconciliationConsumer consumer = BuildConsumer(context);

        int deactivated = await consumer.ProcessAsync(BuildMessage(runStartedAt), CancellationToken.None);

        // A listing import describes one channel; another channel's cards are not evidence of
        // anything about it.
        deactivated.Should().Be(0);
        (await IsLinkActiveAsync(context, _otherChannelId, "999")).Should().BeTrue();
    }

    [Fact]
    public async Task ProcessAsync_MissingRunStart_SkipsTheSweepEntirely()
    {
        string databaseName = Guid.NewGuid().ToString();

        using CatalogDbContext context = BuildCatalogDbContext(databaseName);
        await CreateChannelAsync(context, _channelId);
        await SeedLinkAsync(context, _channelId, "999", DateTimeOffset.UtcNow.AddDays(-30));

        ChannelProductFullSetReconciliationConsumer consumer = BuildConsumer(context);

        // default(DateTimeOffset) is what an event with no boundary deserialises to. Read literally
        // it is "the beginning of time", which would deactivate every link of the channel.
        int deactivated = await consumer.ProcessAsync(
            BuildMessage(default(DateTimeOffset)),
            CancellationToken.None);

        deactivated.Should().Be(0);
        (await IsLinkActiveAsync(context, _channelId, "999")).Should().BeTrue(
            "without a boundary the sweep must not run at all");
    }

    [Fact]
    public async Task ProcessAsync_DeactivatedLink_IsReactivatedByTheNextImport()
    {
        string databaseName = Guid.NewGuid().ToString();
        DateTimeOffset runStartedAt = DateTimeOffset.UtcNow;

        using CatalogDbContext context = BuildCatalogDbContext(databaseName);
        await CreateChannelAsync(context, _channelId);

        // The card was deactivated by an earlier run, then imported again: the import stamped it at
        // its own start and flipped IsActive back on, which is what UpdateExistingProductAsync does.
        Guid linkId = await SeedLinkAsync(
            context, _channelId, "111", runStartedAt.AddSeconds(1), isActive: true);

        ChannelProductFullSetReconciliationConsumer consumer = BuildConsumer(context);

        int deactivated = await consumer.ProcessAsync(BuildMessage(runStartedAt), CancellationToken.None);

        deactivated.Should().Be(0);
        (await IsActiveAsync(context, linkId)).Should().BeTrue(
            "the card is listed again, so the sweep of the run that saw it must not deactivate it");
    }

    private ChannelProductFullSetReconciliationRequestedEvent BuildMessage(DateTimeOffset runStartedAt)
    {
        return new ChannelProductFullSetReconciliationRequestedEvent
        {
            JobRunId = _jobRunId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            RunStartedAt = runStartedAt,
        };
    }

    private static async Task<bool> IsActiveAsync(CatalogDbContext context, Guid linkId)
    {
        ChannelProduct? link = await context.ChannelProducts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(cp => cp.Id == linkId);

        link.Should().NotBeNull();
        return link!.IsActive;
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
    /// Seeds a link with an explicit <paramref name="lastSyncedAt"/>: the sweep reads only that
    /// field, so letting the import own it would make the boundary untestable.
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

    private static async Task CreateChannelAsync(CatalogDbContext context, Guid channelId)
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

    private static ChannelProductFullSetReconciliationConsumer BuildConsumer(CatalogDbContext context)
    {
        return new ChannelProductFullSetReconciliationConsumer(
            context,
            Mock.Of<ILogger<ChannelProductFullSetReconciliationConsumer>>(),
            TimeProvider.System);
    }
}
