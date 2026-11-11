using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Orders;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Tests.ApiClients;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

public class PacedOrdersSourceTests
{
    private readonly FakeTimeProvider _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero));
    private readonly Mock<IOrdersChannelClient> _ordersSource = new();

    public interface IOrdersChannelClient : IChannelApiClient, IOrdersSource { }

    private static Connection BuildConnection()
    {
        return new Connection
        {
            Id = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            ApiKey = "test-key",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            SystemChannelTemplate = new SystemChannelTemplate { Code = "wildberries" },
        };
    }

    private static OrdersRequest BuildRequest()
    {
        DateTimeOffset from = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.FromHours(3));

        return new OrdersRequest { DateFrom = from, DateTo = from.AddDays(1).AddSeconds(-1), Selection = OrdersSelectionMode.Placed, Limit = 0 };
    }

    private static JobRun BuildJobRun(Connection connection)
    {
        return new JobRun { Id = Guid.NewGuid(), ConnectionId = connection.Id };
    }

    private (OrdersImportContext Context, RunCounters Counters) BuildRun(
        JobRun jobRun, Connection connection, DateTimeOffset deadline)
    {
        OrdersImportContext context = OrdersImportContextBuilder.Build(
            jobRun: jobRun,
            connection: connection,
            ordersSource: _ordersSource.Object,
            deadline: deadline);

        RunCounters counters = new RunCounters();

        return (context, counters);
    }

    private PacedOrdersSource CreateSource()
    {
        return new PacedOrdersSource(new AlwaysAllowRateLimiter(), _timeProvider);
    }

    [Fact]
    public async Task GetOrdersAsync_PageReceived_ReturnsThePage()
    {
        Connection connection = BuildConnection();
        JobRun jobRun = BuildJobRun(connection);

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrdersPage { HasMore = false });

        (OrdersImportContext context, RunCounters counters) = BuildRun(
            jobRun, connection, _timeProvider.GetUtcNow().AddMinutes(10));
        PageAttempt attempt = await CreateSource().GetOrdersAsync(
            context, counters, BuildRequest(), CancellationToken.None);

        attempt.Should().BeOfType<PageAttempt.Page>();
        counters.RequestsIssued.Should().Be(1);
    }

    [Fact]
    public async Task GetOrdersAsync_TimeBudgetExhausted_ReturnsTimeBudgetStopInsteadOfThrowing()
    {
        Connection connection = BuildConnection();
        JobRun jobRun = BuildJobRun(connection);

        // A deadline in the past models a run whose time budget ran out before this request.
        (OrdersImportContext context, RunCounters counters) = BuildRun(
            jobRun, connection, _timeProvider.GetUtcNow().AddSeconds(-1));

        PageAttempt attempt = await CreateSource().GetOrdersAsync(
            context, counters, BuildRequest(), CancellationToken.None);

        // A stop is a value, not an exception: the caller decides whether to continue, finalise or
        // schedule a continuation, and an exception would skip that decision entirely.
        attempt.Should().BeOfType<PageAttempt.Stop>()
            .Which.Cause.Should().Be(StopCause.TimeBudget);
        counters.RequestsIssued.Should().Be(0);

        _ordersSource.Verify(
            s => s.GetOrdersAsync(It.IsAny<Guid>(), It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetOrdersAsync_RequestBudgetExhausted_ReturnsRequestBudgetStopInsteadOfThrowing()
    {
        Connection connection = BuildConnection();
        JobRun jobRun = BuildJobRun(connection);

        OrdersImportContext context = OrdersImportContextBuilder.Build(
            jobRun: jobRun,
            connection: connection,
            ordersSource: _ordersSource.Object,
            deadline: _timeProvider.GetUtcNow().AddMinutes(10));
        RunCounters counters = new RunCounters
        {
            RequestsIssued = context.Tuning.MaxRequestsPerRun,
        };

        PageAttempt attempt = await CreateSource().GetOrdersAsync(
            context, counters, BuildRequest(), CancellationToken.None);

        PageAttempt.Stop stop = attempt.Should().BeOfType<PageAttempt.Stop>().Subject;
        stop.Cause.Should().Be(StopCause.RequestBudget);
        stop.ResumeAt.Should().Be(_timeProvider.GetUtcNow());
        counters.RequestsIssued.Should().Be(context.Tuning.MaxRequestsPerRun);

        _ordersSource.Verify(
            s => s.GetOrdersAsync(It.IsAny<Guid>(), It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetOrdersAsync_ThrottledBeyondWaitTolerance_ReturnsThrottleStopWithRetryInstant()
    {
        Connection connection = BuildConnection();
        JobRun jobRun = BuildJobRun(connection);
        DateTimeOffset retryAt = _timeProvider.GetUtcNow().AddHours(3);

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrdersPage { Throttled = true, RetryAt = retryAt });

        (OrdersImportContext context, RunCounters counters) = BuildRun(
            jobRun, connection, _timeProvider.GetUtcNow().AddMinutes(10));
        PageAttempt attempt = await CreateSource().GetOrdersAsync(
            context, counters, BuildRequest(), CancellationToken.None);

        PageAttempt.Stop stop = attempt.Should().BeOfType<PageAttempt.Stop>().Subject;
        stop.Cause.Should().Be(StopCause.Throttle);
        stop.ResumeAt.Should().Be(retryAt);
        counters.InProcessWaits.Should().Be(0);
        counters.RequestsIssued.Should().Be(1);
    }

    [Fact]
    public async Task GetOrdersAsync_ShortThrottle_WaitsInProcessThenReturnsThePage()
    {
        Connection connection = BuildConnection();
        JobRun jobRun = BuildJobRun(connection);
        int call = 0;

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, OrdersRequest _, CancellationToken _) =>
            {
                call++;

                return Task.FromResult(call == 1
                    ? new OrdersPage { Throttled = true, RetryAt = _timeProvider.GetUtcNow().AddSeconds(30) }
                    : new OrdersPage { HasMore = false });
            });

        (OrdersImportContext context, RunCounters counters) = BuildRun(
            jobRun, connection, _timeProvider.GetUtcNow().AddMinutes(10));
        Task<PageAttempt> pending = CreateSource()
            .GetOrdersAsync(context, counters, BuildRequest(), CancellationToken.None);

        pending.IsCompleted.Should().BeFalse();

        _timeProvider.Advance(TimeSpan.FromSeconds(30));

        PageAttempt attempt = await pending;

        attempt.Should().BeOfType<PageAttempt.Page>();
        counters.InProcessWaits.Should().Be(1);
        counters.TotalWaited.Should().Be(TimeSpan.FromSeconds(30));
        counters.RequestsIssued.Should().Be(2);
    }
}
