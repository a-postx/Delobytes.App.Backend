using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Sales.Application.Interfaces;
using Delobytes.App.Backend.Sales.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Sales.Application.Models;
using Delobytes.App.Backend.Sales.Application.Options;
using Delobytes.App.Backend.Sales.Application.Queries.Orders;
using Delobytes.App.Backend.Sales.Contracts.Events;
using Delobytes.App.Backend.Tests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Sales;

/// <summary>
/// Tests for the bounded order line re-publish: it must page deterministically, publish the same
/// change facts as an import, and never run unbounded on the single-CPU host.
/// </summary>
public class RepublishOrderLinesCommandHandlerTests
{
    [Fact]
    public async Task Handle_Page_IsPublishedInChunksWithContinuationToken()
    {
        // Arrange
        FakeRepublishRepository repository = new FakeRepublishRepository(rows: 3, pageSize: 3);
        RecordingSalesEventPublisher publisher = new RecordingSalesEventPublisher();

        RepublishOrderLinesCommandHandler handler = CreateHandler(repository, publisher, chunkSize: 2);

        // Act
        RepublishOrderLinesResponse response = await handler.Handle(
            new RepublishOrderLinesCommand { MaxLines = 3 },
            CancellationToken.None);

        // Assert
        response.PublishedLines.Should().Be(3);
        response.ChunksPublished.Should().Be(2);
        response.NextContinuationToken.Should().Be(repository.LastRowIds[2].ToString());

        publisher.Events.OfType<OrderLinesChangedEvent>()
            .SelectMany(e => e.Lines)
            .Should().HaveCount(3)
            .And.OnlyContain(l => l.CostRelevantChange && !l.IsNew);
    }

    [Fact]
    public async Task Handle_ShortPage_ReturnsNoContinuationToken()
    {
        // Arrange
        FakeRepublishRepository repository = new FakeRepublishRepository(rows: 2, pageSize: 5);
        RecordingSalesEventPublisher publisher = new RecordingSalesEventPublisher();

        RepublishOrderLinesCommandHandler handler = CreateHandler(repository, publisher, chunkSize: 500);

        // Act
        RepublishOrderLinesResponse response = await handler.Handle(
            new RepublishOrderLinesCommand { MaxLines = 5 },
            CancellationToken.None);

        // Assert
        response.PublishedLines.Should().Be(2);
        response.NextContinuationToken.Should().BeNull();
        repository.LastAfterToken.Should().BeNull();
    }

    [Fact]
    public async Task Handle_MaxLinesAboveTheCap_IsClampedToTheHardLimit()
    {
        // Arrange
        FakeRepublishRepository repository = new FakeRepublishRepository(rows: 1, pageSize: 5000);
        RepublishOrderLinesCommandHandler handler = CreateHandler(repository, new RecordingSalesEventPublisher(), 500);

        // Act
        await handler.Handle(new RepublishOrderLinesCommand { MaxLines = 100000 }, CancellationToken.None);

        // Assert
        repository.LastLimit.Should().Be(5000);
    }

    private static RepublishOrderLinesCommandHandler CreateHandler(
        FakeRepublishRepository repository,
        RecordingSalesEventPublisher publisher,
        int chunkSize)
    {
        Mock<ITenantContext> tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.TenantId).Returns(Guid.NewGuid());

        Mock<ITenantTimeService> timeService = new Mock<ITenantTimeService>();
        timeService
            .Setup(t => t.GetTimeZoneAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TimeZoneInfo.Utc);

        return new RepublishOrderLinesCommandHandler(
            repository,
            publisher,
            tenantContext.Object,
            timeService.Object,
            Microsoft.Extensions.Options.Options.Create(new SalesImportOptions { OrderLinesChangedChunkSize = chunkSize }));
    }

    private sealed class FakeRepublishRepository : IOrderLineRepublishRepository
    {
        private readonly int _rowCount;
        private readonly int _pageSize;

        public FakeRepublishRepository(int rows, int pageSize)
        {
            _rowCount = rows;
            _pageSize = pageSize;
            LastRowIds = Enumerable.Range(0, rows)
                .Select(_ => Guid.NewGuid())
                .OrderBy(id => id.ToString(), StringComparer.Ordinal)
                .ToList();
        }

        public List<Guid> LastRowIds { get; }

        public string? LastAfterToken { get; private set; }

        public int LastLimit { get; private set; }

        public Task<IReadOnlyList<RepublishOrderLine>> GetPageAsync(
            Guid? channelId,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtcExclusive,
            string? afterToken,
            int limit,
            CancellationToken cancellationToken)
        {
            LastAfterToken = afterToken;
            LastLimit = limit;

            int take = Math.Min(limit, _pageSize);

            IReadOnlyList<RepublishOrderLine> page = LastRowIds
                .Take(take)
                .Select(id => new RepublishOrderLine
                {
                    OrderLineId = id,
                    OrderId = Guid.NewGuid(),
                    ChannelId = Guid.NewGuid(),
                    ExternalOrderId = "order-1",
                    ExternalLineId = "srid-1",
                    ExternalProductId = "sku-1",
                    Quantity = 1,
                    OrderDate = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
                    Currency = "RUB",
                })
                .ToList();

            return Task.FromResult(page);
        }
    }
}

