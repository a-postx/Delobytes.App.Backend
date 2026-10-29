using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

public class OrderSnapshotMergerTests
{
    private static ChannelOrderLineSnapshot BuildLine(
        string lineKey,
        bool isCancelled = false,
        DateTimeOffset? statusChangedAt = null,
        decimal? channelReportedTotal = 1200m)
    {
        return new ChannelOrderLineSnapshot
        {
            LineKey = lineKey,
            ExternalLineId = lineKey,
            ExternalProductId = $"nm-{lineKey}",
            OfferId = $"SKU-{lineKey}",
            ProductName = "Test Product",
            Quantity = 1,
            UnitPrice = 1000m,
            BuyerUnitPrice = 950m,
            UnitPriceBeforeDiscount = 1200m,
            Currency = "RUB",
            IsCancelled = isCancelled,
            StatusChangedAt = statusChangedAt,
            ChannelReportedTotal = channelReportedTotal,
        };
    }

    private static ChannelOrderSnapshot BuildCart(
        string externalOrderId,
        DateTimeOffset orderDate,
        DateTimeOffset? statusChangedAt,
        params ChannelOrderLineSnapshot[] lines)
    {
        return new ChannelOrderSnapshot
        {
            ExternalOrderId = externalOrderId,
            ExternalOrderNumber = externalOrderId,
            OrderDate = orderDate,
            StatusChangedAt = statusChangedAt,
            ExternalStatus = "new",
            Currency = "RUB",
            Lines = lines.ToList(),
        };
    }

    [Fact]
    public void MergeByExternalOrderId_NoCollisions_ReturnsEachOrderUnchanged()
    {
        ChannelOrderSnapshot first = BuildCart("ORDER-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, BuildLine("SRID-1"));
        ChannelOrderSnapshot second = BuildCart("ORDER-2", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, BuildLine("SRID-2"));

        List<ChannelOrderSnapshot> merged = OrderSnapshotMerger.MergeByExternalOrderId(new[] { first, second });

        merged.Should().HaveCount(2);
        merged.Should().Contain(o => o.ExternalOrderId == "ORDER-1");
        merged.Should().Contain(o => o.ExternalOrderId == "ORDER-2");
    }

    [Fact]
    public void MergeByExternalOrderId_Collision_UnionsLinesOfBothReports()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ChannelOrderSnapshot first = BuildCart("ORDER-1", now.AddDays(-1), now, BuildLine("SRID-1", statusChangedAt: now));
        ChannelOrderSnapshot second = BuildCart("ORDER-1", now.AddDays(-1), now, BuildLine("SRID-2", statusChangedAt: now));

        List<ChannelOrderSnapshot> merged = OrderSnapshotMerger.MergeByExternalOrderId(new[] { first, second });

        merged.Should().ContainSingle();
        merged.Single().Lines.Select(l => l.LineKey).Should().BeEquivalentTo(new[] { "SRID-1", "SRID-2" });
    }

    [Fact]
    public void MergeByExternalOrderId_ReturnsInsertionOrder_CallerAppliesItsOwnOrdering()
    {
        ChannelOrderSnapshot b = BuildCart("ORDER-B", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, BuildLine("SRID-B"));
        ChannelOrderSnapshot a = BuildCart("ORDER-A", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, BuildLine("SRID-A"));

        List<ChannelOrderSnapshot> merged = OrderSnapshotMerger.MergeByExternalOrderId(new[] { b, a });

        merged.Select(o => o.ExternalOrderId).Should().Equal("ORDER-B", "ORDER-A");
    }

    [Fact]
    public void Merge_LaterReportHoldsOnlyCancelledUnit_HeaderStaysActiveBecauseReducedFromMergedLines()
    {
        DateTimeOffset earlier = DateTimeOffset.UtcNow.AddMinutes(-30);
        DateTimeOffset later = DateTimeOffset.UtcNow.AddMinutes(-5);

        ChannelOrderSnapshot existing = BuildCart(
            "ORDER-1", earlier, earlier, BuildLine("SRID-1", isCancelled: false, statusChangedAt: earlier));

        ChannelOrderSnapshot incoming = BuildCart(
            "ORDER-1", earlier, later, BuildLine("SRID-2", isCancelled: true, statusChangedAt: later)) with
        {
            ExternalStatus = "cancelled",
            CancelReason = "cancel",
        };

        ChannelOrderSnapshot merged = OrderSnapshotMerger.Merge(existing, incoming);

        merged.Lines.Select(l => l.LineKey).Should().BeEquivalentTo(new[] { "SRID-1", "SRID-2" });
        merged.ExternalStatus.Should().Be("active");
        merged.CancelReason.Should().BeNull();
        merged.StatusChangedAt.Should().Be(later);
    }

    [Fact]
    public void Merge_BothUnitsCancelled_HeaderReportsCancelled()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        ChannelOrderSnapshot existing = BuildCart("ORDER-1", now, now, BuildLine("SRID-1", isCancelled: true, statusChangedAt: now));
        ChannelOrderSnapshot incoming = BuildCart("ORDER-1", now, now, BuildLine("SRID-2", isCancelled: true, statusChangedAt: now));

        ChannelOrderSnapshot merged = OrderSnapshotMerger.Merge(existing, incoming);

        merged.ExternalStatus.Should().Be("cancelled");
        merged.CancelReason.Should().Be("cancel");
    }

    [Fact]
    public void UnionLines_SameLineKeyBothSides_KeepsLaterVersionOnly()
    {
        DateTimeOffset earlier = DateTimeOffset.UtcNow.AddMinutes(-10);
        DateTimeOffset later = DateTimeOffset.UtcNow;

        List<ChannelOrderLineSnapshot> existing = new List<ChannelOrderLineSnapshot> { BuildLine("SRID-1", statusChangedAt: earlier) };
        List<ChannelOrderLineSnapshot> incoming = new List<ChannelOrderLineSnapshot> { BuildLine("SRID-1", isCancelled: true, statusChangedAt: later) };

        List<ChannelOrderLineSnapshot> merged = OrderSnapshotMerger.UnionLines(existing, incoming);

        merged.Should().ContainSingle();
        merged.Single().IsCancelled.Should().BeTrue();
        merged.Single().StatusChangedAt.Should().Be(later);
    }

    [Fact]
    public void UnionLines_DifferentLineKeys_KeepsBoth()
    {
        List<ChannelOrderLineSnapshot> existing = new List<ChannelOrderLineSnapshot> { BuildLine("SRID-1") };
        List<ChannelOrderLineSnapshot> incoming = new List<ChannelOrderLineSnapshot> { BuildLine("SRID-2") };

        List<ChannelOrderLineSnapshot> merged = OrderSnapshotMerger.UnionLines(existing, incoming);

        merged.Select(l => l.LineKey).Should().BeEquivalentTo(new[] { "SRID-1", "SRID-2" });
    }

    [Fact]
    public void IsLaterLine_CandidateHasNoInstant_NeverDisplacesCurrent()
    {
        ChannelOrderLineSnapshot current = BuildLine("SRID-1", statusChangedAt: DateTimeOffset.UtcNow);
        ChannelOrderLineSnapshot candidate = BuildLine("SRID-1", statusChangedAt: null);

        OrderSnapshotMerger.IsLaterLine(candidate, current).Should().BeFalse();
    }

    [Fact]
    public void IsLaterLine_CurrentHasNoInstantButCandidateDoes_CandidateWins()
    {
        ChannelOrderLineSnapshot current = BuildLine("SRID-1", statusChangedAt: null);
        ChannelOrderLineSnapshot candidate = BuildLine("SRID-1", statusChangedAt: DateTimeOffset.UtcNow);

        OrderSnapshotMerger.IsLaterLine(candidate, current).Should().BeTrue();
    }

    [Fact]
    public void IsLaterLine_EqualInstants_CandidateDoesNotDisplaceCurrent()
    {
        DateTimeOffset instant = DateTimeOffset.UtcNow;
        ChannelOrderLineSnapshot current = BuildLine("SRID-1", statusChangedAt: instant);
        ChannelOrderLineSnapshot candidate = BuildLine("SRID-1", statusChangedAt: instant);

        OrderSnapshotMerger.IsLaterLine(candidate, current).Should().BeFalse();
    }

    [Fact]
    public void IsLaterReport_CandidateStatusChangedAtIsLater_ReturnsTrue()
    {
        DateTimeOffset earlier = DateTimeOffset.UtcNow.AddMinutes(-10);
        DateTimeOffset later = DateTimeOffset.UtcNow;

        ChannelOrderSnapshot current = BuildCart("ORDER-1", earlier, earlier, BuildLine("SRID-1"));
        ChannelOrderSnapshot candidate = BuildCart("ORDER-1", earlier, later, BuildLine("SRID-1"));

        OrderSnapshotMerger.IsLaterReport(candidate, current).Should().BeTrue();
    }

    [Fact]
    public void IsLaterReport_NoStatusChangedAt_FallsBackToOrderDate()
    {
        DateTimeOffset earlier = DateTimeOffset.UtcNow.AddMinutes(-10);
        DateTimeOffset later = DateTimeOffset.UtcNow;

        ChannelOrderSnapshot current = BuildCart("ORDER-1", earlier, null, BuildLine("SRID-1"));
        ChannelOrderSnapshot candidate = BuildCart("ORDER-1", later, null, BuildLine("SRID-1"));

        OrderSnapshotMerger.IsLaterReport(candidate, current).Should().BeTrue();
    }

    [Fact]
    public void IsLaterReport_ExactTie_ReturnsFalseAndKeepsCurrent()
    {
        DateTimeOffset instant = DateTimeOffset.UtcNow;

        ChannelOrderSnapshot current = BuildCart("ORDER-1", instant, instant, BuildLine("SRID-1"));
        ChannelOrderSnapshot candidate = BuildCart("ORDER-1", instant, instant, BuildLine("SRID-1"));

        OrderSnapshotMerger.IsLaterReport(candidate, current).Should().BeFalse();
    }
}
