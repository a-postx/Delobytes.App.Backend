using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;
using Delobytes.App.Backend.Integrations.Tests.ApiClients;
using FluentAssertions;
using Delobytes.App.Backend.Integrations.Application.Options;
using Microsoft.Extensions.Options;
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
        };
    }

    private static OrdersRequest BuildRequest()
    {
        DateTimeOffset from = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.FromHours(3));

        return new OrdersRequest { DateFrom = from, DateTo = from.AddDays(1).AddSeconds(-1), Flag = 1, Limit = 0 };
    }

    private PacedOrdersSource CreateSource(TimeSpan? maxInProcessWait = null)
    {
        TimeSpan wait = maxInProcessWait ?? TimeSpan.Zero;
        WildberriesImportOptions options = new WildberriesImportOptions
        {
            MaxInProcessWaitSeconds = (int)wait.TotalSeconds,
        };

        return new PacedOrdersSource(
            new AlwaysAllowWildberriesRateLimiter(),
            _timeProvider,
            Options.Create(options));
    }

    [Fact]
    public async Task GetOrdersAsync_PageReceived_ReportsCompletedAttempt()
    {
        Connection connection = BuildConnection();

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrdersPage { HasMore = false });

        OrdersImportRunState state = new OrdersImportRunState(_timeProvider.GetUtcNow().AddMinutes(10));
        OrdersAttempt attempt = await CreateSource().GetOrdersAsync(
            _ordersSource.Object, connection, BuildRequest(), state, CancellationToken.None);

        attempt.IsStopped.Should().BeFalse();
        attempt.Page.Should().NotBeNull();
        attempt.StopReason.Should().Be(StopReason.Completed);
        attempt.ResumeAt.Should().BeNull();
        state.RequestsIssued.Should().Be(1);
    }

    [Fact]
    public async Task GetOrdersAsync_BudgetExhausted_ReturnsBudgetStopInsteadOfThrowing()
    {
        Connection connection = BuildConnection();

        // A deadline in the past models a run whose budget ran out before this request.
        OrdersImportRunState state = new OrdersImportRunState(_timeProvider.GetUtcNow().AddSeconds(-1));

        OrdersAttempt attempt = await CreateSource().GetOrdersAsync(
            _ordersSource.Object, connection, BuildRequest(), state, CancellationToken.None);

        // A stop is a value, not an exception: the caller decides whether to continue, finalise or
        // schedule a continuation, and an exception would skip that decision entirely.
        attempt.IsStopped.Should().BeTrue();
        attempt.StopReason.Should().Be(StopReason.Budget);
        attempt.ResumeAt.Should().NotBeNull();
        state.RequestsIssued.Should().Be(0);

        _ordersSource.Verify(
            s => s.GetOrdersAsync(It.IsAny<Guid>(), It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetOrdersAsync_ThrottledBeyondWaitTolerance_ReturnsThrottleStopWithRetryInstant()
    {
        Connection connection = BuildConnection();
        DateTimeOffset retryAt = _timeProvider.GetUtcNow().AddHours(3);

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrdersPage { Throttled = true, RetryAt = retryAt });

        OrdersImportRunState state = new OrdersImportRunState(_timeProvider.GetUtcNow().AddMinutes(10));
        OrdersAttempt attempt = await CreateSource().GetOrdersAsync(
            _ordersSource.Object, connection, BuildRequest(), state, CancellationToken.None);

        attempt.IsStopped.Should().BeTrue();
        attempt.StopReason.Should().Be(StopReason.Throttled);
        attempt.ResumeAt.Should().Be(retryAt);
        state.InProcessWaits.Should().Be(0);
        state.RequestsIssued.Should().Be(1);
    }

    [Fact]
    public async Task GetOrdersAsync_ShortThrottle_WaitsInProcessThenReturnsThePage()
    {
        Connection connection = BuildConnection();
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

        OrdersImportRunState state = new OrdersImportRunState(_timeProvider.GetUtcNow().AddMinutes(10));
        Task<OrdersAttempt> pending = CreateSource(maxInProcessWait: TimeSpan.FromMinutes(1))
            .GetOrdersAsync(_ordersSource.Object, connection, BuildRequest(), state, CancellationToken.None);

        pending.IsCompleted.Should().BeFalse();

        _timeProvider.Advance(TimeSpan.FromSeconds(30));

        OrdersAttempt attempt = await pending;

        attempt.IsStopped.Should().BeFalse();
        state.InProcessWaits.Should().Be(1);
        state.TotalWaited.Should().Be(TimeSpan.FromSeconds(30));
        state.RequestsIssued.Should().Be(2);
    }
}
