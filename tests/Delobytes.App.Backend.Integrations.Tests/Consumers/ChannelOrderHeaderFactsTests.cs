using Delobytes.App.Backend.Integrations.Contracts.Models;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

public class ChannelOrderHeaderFactsTests
{
    private static ChannelOrderLineSnapshot BuildLine(bool isCancelled)
    {
        return new ChannelOrderLineSnapshot
        {
            LineKey = "LINE-1",
            ExternalLineId = "LINE-1",
            ExternalProductId = "PRODUCT-1",
            OfferId = "SKU-1",
            ProductName = "Test Product",
            Quantity = 1,
            UnitPrice = 1000m,
            BuyerUnitPrice = 950m,
            UnitPriceBeforeDiscount = 1200m,
            Currency = "RUB",
            IsCancelled = isCancelled,
        };
    }

    [Fact]
    public void Reduce_NoReportedStatus_NotAllCancelled_YieldsActive()
    {
        ChannelOrderLineSnapshot[] lines = { BuildLine(false), BuildLine(false) };

        ChannelOrderHeaderFacts facts = ChannelOrderHeaderFacts.Reduce(lines, new[] { DateTimeOffset.UtcNow });

        facts.ExternalStatus.Should().Be("active");
        facts.CancelReason.Should().BeNull();
    }

    [Fact]
    public void Reduce_NoReportedStatus_AllCancelled_YieldsCancelled()
    {
        ChannelOrderLineSnapshot[] lines = { BuildLine(true), BuildLine(true) };

        ChannelOrderHeaderFacts facts = ChannelOrderHeaderFacts.Reduce(lines, new[] { DateTimeOffset.UtcNow });

        facts.ExternalStatus.Should().Be("cancelled");
        facts.CancelReason.Should().Be("cancel");
    }

    [Fact]
    public void Reduce_ReportedStatusAndNotAllLinesCancelled_ReportedStatusWins()
    {
        ChannelOrderLineSnapshot[] lines = { BuildLine(true), BuildLine(false) };

        ChannelOrderHeaderFacts facts = ChannelOrderHeaderFacts.Reduce(
            lines,
            new[] { DateTimeOffset.UtcNow },
            reportedHeaderStatus: "awaiting_deliver",
            reportedCancelReason: "buyer_cancelled");

        facts.ExternalStatus.Should().Be("awaiting_deliver");
        facts.CancelReason.Should().BeNull();
    }

    [Fact]
    public void Reduce_AllLinesCancelled_ReportedStatusIsIgnoredAndCancelledWins()
    {
        ChannelOrderLineSnapshot[] lines = { BuildLine(true), BuildLine(true) };

        ChannelOrderHeaderFacts facts = ChannelOrderHeaderFacts.Reduce(
            lines,
            new[] { DateTimeOffset.UtcNow },
            reportedHeaderStatus: "awaiting_deliver",
            reportedCancelReason: "buyer_cancelled");

        facts.ExternalStatus.Should().Be("cancelled");
        facts.CancelReason.Should().Be("buyer_cancelled");
    }

    [Fact]
    public void Reduce_AllLinesCancelled_NoReportedCancelReason_FallsBackToDefault()
    {
        ChannelOrderLineSnapshot[] lines = { BuildLine(true) };

        ChannelOrderHeaderFacts facts = ChannelOrderHeaderFacts.Reduce(
            lines,
            new[] { DateTimeOffset.UtcNow },
            reportedHeaderStatus: "awaiting_deliver");

        facts.ExternalStatus.Should().Be("cancelled");
        facts.CancelReason.Should().Be("cancel");
    }

    [Fact]
    public void Reduce_BlankReportedStatus_FallsBackToActive()
    {
        ChannelOrderLineSnapshot[] lines = { BuildLine(false) };

        ChannelOrderHeaderFacts facts = ChannelOrderHeaderFacts.Reduce(
            lines,
            new[] { DateTimeOffset.UtcNow },
            reportedHeaderStatus: "   ");

        facts.ExternalStatus.Should().Be("active");
        facts.CancelReason.Should().BeNull();
    }
}
