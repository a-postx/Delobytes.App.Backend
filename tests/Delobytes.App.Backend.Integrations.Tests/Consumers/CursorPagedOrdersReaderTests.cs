using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;
using Delobytes.App.Backend.Integrations.Tests.ApiClients;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

/// <summary>
/// Tests for <see cref="CursorPagedOrdersReader"/>: a window is followed to its last page and
/// returned as one merged set, or reported as a stop that the caller must not publish.
/// </summary>
public class CursorPagedOrdersReaderTests
{
    private static readonly DateTimeOffset WindowFrom = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.FromHours(3));

    private readonly FakeTimeProvider _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero));

    private sealed class ScriptedOrdersSource : IOrdersSource
    {
        private readonly Func<OrdersRequest, OrdersPage> _pageProvider;

        public ScriptedOrdersSource(Func<OrdersRequest, OrdersPage> pageProvider)
        {
            _pageProvider = pageProvider;
        }

        public List<OrdersRequest> Requests { get; } = new();

        public Task<OrdersPage> GetOrdersAsync(Guid connectionId, OrdersRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            return Task.FromResult(_pageProvider(request));
        }
    }

    private static Connection BuildConnection()
    {
        return new Connection
        {
            Id = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            ApiKey = "client-id",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            SystemChannelTemplate = new SystemChannelTemplate { Code = "ozon" },
        };
    }

    private static OrdersRequest BuildRequest(string? cursor = null)
    {
        return new OrdersRequest
        {
            DateFrom = WindowFrom,
            DateTo = WindowFrom.AddDays(1).AddSeconds(-1),
            Selection = OrdersSelectionMode.Placed,
            Limit = 1000,
            Cursor = cursor,
            Schema = OrdersSchema.Fbo,
        };
    }

    private static ChannelOrderSnapshot BuildOrder(string externalOrderId, string lineKey)
    {
        return new ChannelOrderSnapshot
        {
            ExternalOrderId = externalOrderId,
            ExternalOrderNumber = externalOrderId,
            OrderDate = WindowFrom.AddHours(10),
            StatusChangedAt = WindowFrom.AddHours(11),
            ExternalStatus = "awaiting_packaging",
            Currency = "RUB",
            Lines = new List<ChannelOrderLineSnapshot>
            {
                new ChannelOrderLineSnapshot
                {
                    LineKey = lineKey,
                    ExternalLineId = lineKey,
                    ExternalProductId = $"sku-{lineKey}",
                    OfferId = $"SKU-{lineKey}",
                    ProductName = "Test Product",
                    Quantity = 1,
                    UnitPrice = 1000m,
                    BuyerUnitPrice = 950m,
                    UnitPriceBeforeDiscount = 1200m,
                    Currency = "RUB",
                    IsCancelled = false,
                    StatusChangedAt = WindowFrom.AddHours(11),
                    ChannelReportedTotal = 1200m,
                },
            },
        };
    }

    private (OrdersImportContext Context, OrdersRunProgress Progress) BuildRun(
        ScriptedOrdersSource source, DateTimeOffset deadline)
    {
        Connection connection = BuildConnection();

        JobRun jobRun = new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            JobType = JobType.OrdersImportFbo,
            Status = JobStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            Connection = connection,
        };

        OrdersImportContext context = new OrdersImportContext(
            jobRun,
            connection,
            source,
            new OrdersBackfillState(),
            deadline,
            WindowFrom.AddDays(1),
            false,
            OrdersSchema.Fbo);

        return (context, new OrdersRunProgress(0));
    }

    private CursorPagedOrdersReader CreateReader(TimeSpan? maxInProcessWait = null)
    {
        PacedOrdersSource pacedSource = new PacedOrdersSource(new AlwaysAllowOrdersRateLimiter(), _timeProvider);

        return new CursorPagedOrdersReader(
            pacedSource,
            NullLogger<CursorPagedOrdersReader>.Instance,
            maxInProcessWait ?? TimeSpan.Zero,
            OrdersRateLimiterMethods.OzonPostingFboList);
    }

    [Fact]
    public async Task ReadWindowAsync_TwoPages_MergesThemIntoOneDeduplicatedSet()
    {
        ScriptedOrdersSource source = new ScriptedOrdersSource(request =>
        {
            if (request.Cursor == null)
            {
                return new OrdersPage
                {
                    Orders = new List<ChannelOrderSnapshot> { BuildOrder("ORDER-1", "LINE-1"), BuildOrder("ORDER-2", "LINE-2") },
                    HasMore = true,
                    NextCursor = "page-2",
                };
            }

            // ORDER-2 is legitimately reported again on the boundary of the next page; it is one
            // order observed twice, not two orders.
            return new OrdersPage
            {
                Orders = new List<ChannelOrderSnapshot> { BuildOrder("ORDER-2", "LINE-3"), BuildOrder("ORDER-3", "LINE-4") },
                HasMore = false,
                NextCursor = null,
            };
        });

        (OrdersImportContext context, OrdersRunProgress progress) = BuildRun(source, _timeProvider.GetUtcNow().AddMinutes(10));

        FetchOutcome outcome = await CreateReader().ReadWindowAsync(BuildRequest(), context, progress, CancellationToken.None);

        outcome.IsStopped.Should().BeFalse();
        outcome.StopReason.Should().Be(StopReason.Completed);
        outcome.Orders.Select(o => o.ExternalOrderId).Should().BeEquivalentTo(new[] { "ORDER-1", "ORDER-2", "ORDER-3" });
        outcome.Orders.Single(o => o.ExternalOrderId == "ORDER-2").Lines.Select(l => l.LineKey)
            .Should().BeEquivalentTo(new[] { "LINE-2", "LINE-3" });
        source.Requests.Should().HaveCount(2);
        source.Requests[0].Cursor.Should().BeNull();
        source.Requests[1].Cursor.Should().Be("page-2");
        outcome.RequestsIssued.Should().Be(2);
    }

    [Fact]
    public async Task ReadWindowAsync_HasMoreWithBlankCursor_Throws()
    {
        ScriptedOrdersSource source = new ScriptedOrdersSource(_ => new OrdersPage
        {
            Orders = new List<ChannelOrderSnapshot> { BuildOrder("ORDER-1", "LINE-1") },
            HasMore = true,
            NextCursor = " ",
        });

        (OrdersImportContext context, OrdersRunProgress progress) = BuildRun(source, _timeProvider.GetUtcNow().AddMinutes(10));

        Func<Task> act = async () => await CreateReader().ReadWindowAsync(
            BuildRequest(), context, progress, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        source.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task ReadWindowAsync_HasMoreWithUnchangedCursor_Throws()
    {
        ScriptedOrdersSource source = new ScriptedOrdersSource(_ => new OrdersPage
        {
            Orders = new List<ChannelOrderSnapshot> { BuildOrder("ORDER-1", "LINE-1") },
            HasMore = true,
            NextCursor = "page-1",
        });

        (OrdersImportContext context, OrdersRunProgress progress) = BuildRun(source, _timeProvider.GetUtcNow().AddMinutes(10));

        Func<Task> act = async () => await CreateReader().ReadWindowAsync(
            BuildRequest("page-1"), context, progress, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        source.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task ReadWindowAsync_ThrottledOnSecondPage_ReturnsStopWithNoOrders()
    {
        ScriptedOrdersSource source = new ScriptedOrdersSource(request =>
        {
            if (request.Cursor == null)
            {
                return new OrdersPage
                {
                    Orders = new List<ChannelOrderSnapshot> { BuildOrder("ORDER-1", "LINE-1") },
                    HasMore = true,
                    NextCursor = "page-2",
                };
            }

            return new OrdersPage
            {
                Throttled = true,
                RetryAt = _timeProvider.GetUtcNow().AddMinutes(5),
            };
        });

        (OrdersImportContext context, OrdersRunProgress progress) = BuildRun(source, _timeProvider.GetUtcNow().AddMinutes(60));

        FetchOutcome outcome = await CreateReader().ReadWindowAsync(BuildRequest(), context, progress, CancellationToken.None);

        outcome.IsStopped.Should().BeTrue();
        outcome.StopReason.Should().Be(StopReason.Throttled);
        outcome.Orders.Should().BeEmpty("a stopped window must publish nothing, even the page it already read");
        outcome.OrdersFetched.Should().Be(0);
    }

    [Fact]
    public async Task ReadWindowAsync_RowsWithoutIdentity_Throws()
    {
        ScriptedOrdersSource source = new ScriptedOrdersSource(_ => new OrdersPage
        {
            Orders = new List<ChannelOrderSnapshot> { BuildOrder("ORDER-1", "LINE-1") },
            HasMore = false,
            RowsWithoutIdentity = 2,
        });

        (OrdersImportContext context, OrdersRunProgress progress) = BuildRun(source, _timeProvider.GetUtcNow().AddMinutes(10));

        Func<Task> act = async () => await CreateReader().ReadWindowAsync(
            BuildRequest(), context, progress, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ReadWindowAsync_BudgetExhausted_ReturnsBudgetStop()
    {
        ScriptedOrdersSource source = new ScriptedOrdersSource(_ => throw new InvalidOperationException("no request may be issued past the run deadline"));

        (OrdersImportContext context, OrdersRunProgress progress) = BuildRun(
            source, _timeProvider.GetUtcNow().AddSeconds(-1));

        FetchOutcome outcome = await CreateReader().ReadWindowAsync(BuildRequest(), context, progress, CancellationToken.None);

        outcome.IsStopped.Should().BeTrue();
        outcome.StopReason.Should().Be(StopReason.Budget);
        outcome.Orders.Should().BeEmpty();
        source.Requests.Should().BeEmpty();
    }
}
