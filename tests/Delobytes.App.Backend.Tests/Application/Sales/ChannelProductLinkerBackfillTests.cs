using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Contracts.Events;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Sales.Application.Options;
using Delobytes.App.Backend.Sales.Application.Services;
using Delobytes.App.Backend.Sales.Contracts.Events;
using Delobytes.App.Backend.Sales.Domain.Entities;
using Delobytes.App.Backend.Sales.Infrastructure.Persistence;
using Delobytes.App.Backend.Sales.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Sales;

/// <summary>
/// Tests for the order line backfill performed when Catalog publishes a channel product link. A
/// line linked to a deleted stale projection must be repaired, not only a line with no link: after
/// a relink the old identifier has no projection row left, so Catalog could never resolve its cost.
/// </summary>
public class ChannelProductLinkerBackfillTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ChannelId = Guid.NewGuid();
    private static readonly Guid ChannelProductId = Guid.NewGuid();

    [Fact]
    public async Task Link_LineWithoutLink_IsFilledAndRepublished()
    {
        await RunBackfillAsync(staleChannelProductId: null);
    }

    [Fact]
    public async Task Link_LinePointingAtDeletedProjection_IsRepairedAndRepublished()
    {
        await RunBackfillAsync(staleChannelProductId: Guid.NewGuid());
    }

    private static async Task RunBackfillAsync(Guid? staleChannelProductId)
    {
        DbContextOptions<SalesDbContext> options = new DbContextOptionsBuilder<SalesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        Mock<ITenantContext> tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.TenantId).Returns(TenantId);

        await using SalesDbContext context = new SalesDbContext(options, tenantContext.Object);

        Order order = new Order
        {
            Id = Guid.NewGuid(),
            ChannelId = ChannelId,
            ExternalOrderId = "order-1",
            OrderDate = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            Currency = "RUB",
            ExternalStatus = "delivered",
            FirstImportedAt = DateTimeOffset.UtcNow,
            LastImportedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        OrderLine line = new OrderLine
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            LineKey = "line-1",
            ExternalLineId = "srid-1",
            ExternalProductId = "sku-1",
            Quantity = 1,
            Currency = "RUB",
            ChannelProductId = staleChannelProductId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        order.Lines.Add(line);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        RecordingPublisher publisher = new RecordingPublisher();

        ChannelProductLinker linker = new ChannelProductLinker(
            new ChannelProductRefRepository(context),
            publisher,
            NullLogger<ChannelProductLinker>.Instance,
            TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new SalesImportOptions()));

        await linker.UpdateChannelProductRefAsync(
            new ChannelProductLinkedEvent
            {
                ChannelProductId = ChannelProductId,
                ProductId = Guid.NewGuid(),
                ChannelId = ChannelId,
                ExternalProductId = "sku-1",
            },
            CancellationToken.None);

        line.ChannelProductId.Should().Be(ChannelProductId);

        OrderLinesChangedEvent published = publisher.Events
            .OfType<OrderLinesChangedEvent>()
            .Should().ContainSingle().Subject;

        published.Lines.Should().ContainSingle();
        published.Lines[0].OrderLineId.Should().Be(line.Id);
        published.Lines[0].ChannelProductId.Should().Be(ChannelProductId);
        published.Lines[0].CostRelevantChange.Should().BeTrue();
        published.Lines[0].IsNew.Should().BeFalse();
    }

    private sealed class RecordingPublisher : Delobytes.App.Backend.Sales.Application.Interfaces.IEventPublisher
    {
        public List<object> Events { get; } = new List<object>();

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
            where TEvent : class
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }
    }
}

