using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Contracts.Events;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Sales.Domain.Entities;
using Delobytes.App.Backend.Sales.Infrastructure.Messaging.Consumers;
using Delobytes.App.Backend.Sales.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Sales;

/// <summary>
/// Covers the Sales-side <see cref="ChannelProductRef"/> projection, which is what
/// <see cref="OrderLine.ChannelProductId"/> is resolved from during an order import. Nothing wrote
/// this table before, so every order line carried a null link; these tests pin the write path.
/// </summary>
public class ChannelProductLinkedConsumerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _channelId = Guid.NewGuid();
    private readonly Guid _productId = Guid.NewGuid();
    private readonly Guid _channelProductId = Guid.NewGuid();

    [Fact]
    public async Task ProcessAsync_NewLink_CreatesProjectionWithEveryField()
    {
        string databaseName = Guid.NewGuid().ToString();
        using SalesDbContext context = BuildSalesDbContext(databaseName);
        ChannelProductLinkedConsumer consumer = BuildConsumer(context);

        DateTimeOffset before = DateTimeOffset.UtcNow;

        await consumer.ProcessAsync(
            BuildLinkedEvent(photoUrl: "https://cdn.example/photo.jpg"),
            CancellationToken.None);

        ChannelProductRef? projection = await context.ChannelProductRefs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.ChannelProductId == _channelProductId);

        projection.Should().NotBeNull();
        projection!.ProductId.Should().Be(_productId);
        projection.ChannelId.Should().Be(_channelId);
        projection.ExternalProductId.Should().Be("123456789");
        projection.ExternalSku.Should().Be("ART-1");
        projection.Sku.Should().Be("SKU-1");
        projection.ProductName.Should().Be("Product name from the catalogue");
        projection.PhotoUrl.Should().Be("https://cdn.example/photo.jpg");
        projection.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task ProcessAsync_RepublishedLink_UpdatesExistingRowInsteadOfDuplicating()
    {
        string databaseName = Guid.NewGuid().ToString();
        using SalesDbContext context = BuildSalesDbContext(databaseName);
        ChannelProductLinkedConsumer consumer = BuildConsumer(context);

        await consumer.ProcessAsync(BuildLinkedEvent(), CancellationToken.None);

        // The next import of the same card re-publishes the link with refreshed card data.
        await consumer.ProcessAsync(
            BuildLinkedEvent(productName: "Renamed product", photoUrl: "https://cdn.example/new.jpg"),
            CancellationToken.None);

        context.ChannelProductRefs.IgnoreQueryFilters().Should().ContainSingle(
            "the projection is keyed by ChannelProductId and republishing must be idempotent");

        ChannelProductRef projection = await context.ChannelProductRefs
            .IgnoreQueryFilters()
            .SingleAsync(r => r.ChannelProductId == _channelProductId);

        projection.ProductName.Should().Be("Renamed product");
        projection.PhotoUrl.Should().Be("https://cdn.example/new.jpg");
    }

    [Fact]
    public async Task ProcessAsync_NewChannelProductIdForSameExternalProduct_ReplacesStaleRow()
    {
        string databaseName = Guid.NewGuid().ToString();
        using SalesDbContext context = BuildSalesDbContext(databaseName);
        ChannelProductLinkedConsumer consumer = BuildConsumer(context);

        await consumer.ProcessAsync(BuildLinkedEvent(), CancellationToken.None);

        // A relink issues a new ChannelProduct identity for the same marketplace item. The unique
        // index on (TenantId, ChannelId, ExternalProductId) would reject the insert while the old
        // row still occupies it, so the old row has to go in the same pass.
        Guid relinkedChannelProductId = Guid.NewGuid();

        await consumer.ProcessAsync(
            BuildLinkedEvent(channelProductId: relinkedChannelProductId, productName: "Relinked product"),
            CancellationToken.None);

        context.ChannelProductRefs.IgnoreQueryFilters().Should().ContainSingle(
            "two rows cannot share the natural key of one marketplace item");

        ChannelProductRef projection = await context.ChannelProductRefs
            .IgnoreQueryFilters()
            .SingleAsync();

        projection.ChannelProductId.Should().Be(relinkedChannelProductId);
        projection.ProductName.Should().Be("Relinked product");
    }

    [Fact]
    public async Task ProcessAsync_TwoChannels_SameExternalProductId_KeepsBothRows()
    {
        string databaseName = Guid.NewGuid().ToString();
        using SalesDbContext context = BuildSalesDbContext(databaseName);
        ChannelProductLinkedConsumer consumer = BuildConsumer(context);

        Guid otherChannelId = Guid.NewGuid();

        await consumer.ProcessAsync(BuildLinkedEvent(), CancellationToken.None);
        await consumer.ProcessAsync(
            BuildLinkedEvent(channelProductId: Guid.NewGuid(), channelId: otherChannelId),
            CancellationToken.None);

        context.ChannelProductRefs.IgnoreQueryFilters().Should().HaveCount(2,
            "the natural key includes the channel, so the same external id on two channels is two links");
    }

    [Fact]
    public async Task ProcessAsync_ProjectionIsVisibleToTheOrderImportLookup()
    {
        string databaseName = Guid.NewGuid().ToString();
        using SalesDbContext context = BuildSalesDbContext(databaseName);
        ChannelProductLinkedConsumer consumer = BuildConsumer(context);

        await consumer.ProcessAsync(BuildLinkedEvent(), CancellationToken.None);

        // The exact query OrdersBatchImportRequestedConsumer.LoadChannelProductIdsAsync issues.
        var resolved = await context.ChannelProductRefs
            .AsNoTracking()
            .Where(r => r.ChannelId == _channelId && r.ExternalProductId == "123456789")
            .ToDictionaryAsync(r => r.ExternalProductId, r => r.ChannelProductId, StringComparer.Ordinal);

        resolved.Should().ContainKey("123456789");
        resolved["123456789"].Should().Be(_channelProductId);
    }

    private ChannelProductLinkedEvent BuildLinkedEvent(
        Guid? channelProductId = null,
        Guid? channelId = null,
        string productName = "Product name from the catalogue",
        string? photoUrl = null)
    {
        return new ChannelProductLinkedEvent
        {
            ChannelProductId = channelProductId ?? _channelProductId,
            ProductId = _productId,
            ChannelId = channelId ?? _channelId,
            ExternalProductId = "123456789",
            ExternalSku = "ART-1",
            Sku = "SKU-1",
            ProductName = productName,
            PhotoUrl = photoUrl,
        };
    }

    private static ChannelProductLinkedConsumer BuildConsumer(SalesDbContext context)
    {
        return new ChannelProductLinkedConsumer(
            context,
            Mock.Of<ILogger<ChannelProductLinkedConsumer>>(),
            TimeProvider.System);
    }

    private SalesDbContext BuildSalesDbContext(string databaseName)
    {
        DbContextOptions<SalesDbContext> options = new DbContextOptionsBuilder<SalesDbContext>()
            .UseInMemoryDatabase(databaseName: databaseName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        Mock<ITenantContext> tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(tc => tc.TenantId).Returns(_tenantId);

        return new SalesDbContext(options, tenantContextMock.Object);
    }
}
