using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

public class OrdersWindowPlannerTests
{
    private static OrdersBackfillState BuildBackfillState(
        DateTimeOffset? handoverWatermark = null,
        DateTimeOffset? oldestDayInScope = null)
    {
        return new OrdersBackfillState
        {
            Id = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            OldestDayInScope = oldestDayInScope ?? DateTimeOffset.UtcNow,
            HandoverWatermark = handoverWatermark,
        };
    }

    private static ChannelOrderSnapshot BuildOrder(DateTimeOffset? statusChangedAt)
    {
        return new ChannelOrderSnapshot
        {
            ExternalOrderId = Guid.NewGuid().ToString(),
            ExternalOrderNumber = "1",
            OrderDate = DateTimeOffset.UtcNow,
            StatusChangedAt = statusChangedAt,
            ExternalStatus = "new",
            Currency = "RUB",
        };
    }

    [Fact]
    public void GetMoscowDayStart_ReturnsMidnightAtMoscowOffset()
    {
        DateTimeOffset instant = new DateTimeOffset(2024, 6, 15, 23, 30, 0, TimeSpan.Zero);

        DateTimeOffset dayStart = OrdersWindowPlanner.GetMoscowDayStart(instant);

        dayStart.Should().Be(new DateTimeOffset(2024, 6, 16, 0, 0, 0, OrdersWindowPlanner.MoscowOffset));
    }

    [Fact]
    public void GetMoscowDayStart_IsIdempotentOnADayBoundary()
    {
        DateTimeOffset dayStart = new DateTimeOffset(2024, 6, 16, 0, 0, 0, OrdersWindowPlanner.MoscowOffset);

        OrdersWindowPlanner.GetMoscowDayStart(dayStart).Should().Be(dayStart);
    }

    [Fact]
    public void ResolveDeltaWindowFrom_SyncCheckpointPresent_UsesLastChangedAtMinusSafetyOverlap()
    {
        DateTimeOffset lastChangedAt = DateTimeOffset.UtcNow;
        SyncCheckpoint checkpoint = new SyncCheckpoint { LastChangedAt = lastChangedAt };
        OrdersBackfillState backfillState = BuildBackfillState();

        DateTimeOffset windowFrom = OrdersWindowPlanner.ResolveDeltaWindowFrom(
            checkpoint, backfillState, out bool usedLegacyFallback);

        windowFrom.Should().Be(lastChangedAt - ProcessOrdersImportConsumer.SafetyOverlap);
        usedLegacyFallback.Should().BeFalse();
    }

    [Fact]
    public void ResolveDeltaWindowFrom_NoCheckpointWithHandoverWatermark_UsesHandoverWatermark()
    {
        DateTimeOffset handoverWatermark = DateTimeOffset.UtcNow;
        OrdersBackfillState backfillState = BuildBackfillState(handoverWatermark);

        DateTimeOffset windowFrom = OrdersWindowPlanner.ResolveDeltaWindowFrom(
            null, backfillState, out bool usedLegacyFallback);

        windowFrom.Should().Be(handoverWatermark);
        usedLegacyFallback.Should().BeFalse();
    }

    [Fact]
    public void ResolveDeltaWindowFrom_NoCheckpointAndNoHandoverWatermark_FallsBackToWalkStartAndFlagsLegacy()
    {
        DateTimeOffset oldestDayInScope = DateTimeOffset.UtcNow;
        OrdersBackfillState backfillState = BuildBackfillState(oldestDayInScope: oldestDayInScope);

        DateTimeOffset windowFrom = OrdersWindowPlanner.ResolveDeltaWindowFrom(
            null, backfillState, out bool usedLegacyFallback);

        windowFrom.Should().Be(oldestDayInScope + ProcessOrdersImportConsumer.BackfillWindow);
        usedLegacyFallback.Should().BeTrue();
    }

    [Fact]
    public void ResolveDeltaWindowFrom_CheckpointWithoutLastChangedAt_FallsThroughToBackfillState()
    {
        SyncCheckpoint checkpoint = new SyncCheckpoint { LastChangedAt = null };
        DateTimeOffset handoverWatermark = DateTimeOffset.UtcNow;
        OrdersBackfillState backfillState = BuildBackfillState(handoverWatermark);

        DateTimeOffset windowFrom = OrdersWindowPlanner.ResolveDeltaWindowFrom(
            checkpoint, backfillState, out bool usedLegacyFallback);

        windowFrom.Should().Be(handoverWatermark);
        usedLegacyFallback.Should().BeFalse();
    }

    [Fact]
    public void GetMaxChangedAt_PicksLatestStatusChangedAt_AndIgnoresNullOnes()
    {
        DateTimeOffset windowTo = DateTimeOffset.UtcNow;
        DateTimeOffset earlier = windowTo.AddMinutes(-10);
        DateTimeOffset later = windowTo.AddMinutes(-1);

        List<ChannelOrderSnapshot> orders = new List<ChannelOrderSnapshot>
        {
            BuildOrder(earlier),
            BuildOrder(null),
            BuildOrder(later),
        };

        DateTimeOffset? maxChangedAt = OrdersWindowPlanner.GetMaxChangedAt(orders, windowTo, out DateTimeOffset? observedBeyondWindow);

        maxChangedAt.Should().Be(later.ToUniversalTime());
        observedBeyondWindow.Should().BeNull();
    }

    [Fact]
    public void GetMaxChangedAt_NoOrders_ReturnsNull()
    {
        DateTimeOffset? maxChangedAt = OrdersWindowPlanner.GetMaxChangedAt(
            new List<ChannelOrderSnapshot>(), DateTimeOffset.UtcNow, out DateTimeOffset? observedBeyondWindow);

        maxChangedAt.Should().BeNull();
        observedBeyondWindow.Should().BeNull();
    }

    [Fact]
    public void GetMaxChangedAt_ObservedLaterThanWindowTo_ClampsAndReportsTheObservedValue()
    {
        DateTimeOffset windowTo = DateTimeOffset.UtcNow;
        DateTimeOffset beyondWindow = windowTo.AddMinutes(5);

        List<ChannelOrderSnapshot> orders = new List<ChannelOrderSnapshot> { BuildOrder(beyondWindow) };

        DateTimeOffset? maxChangedAt = OrdersWindowPlanner.GetMaxChangedAt(orders, windowTo, out DateTimeOffset? observedBeyondWindow);

        maxChangedAt.Should().Be(windowTo);
        observedBeyondWindow.Should().Be(beyondWindow);
    }
}
