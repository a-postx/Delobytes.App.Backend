using System;
using System.Collections.Generic;
using Delobytes.App.Backend.Finance.Domain.Calculation;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Finance;

/// <summary>
/// Tests for PnlLineAggregator. The journal is append-only and money arrives in parts, so these
/// tests pin the three rules the totals depend on: the sign comes from the direction rather than
/// from the amount, a superseded version does not count, and a control total is a check rather than
/// an addend.
/// </summary>
public class PnlLineAggregatorTests
{
    [Fact]
    public void Calculate_ComponentsInBothDirections_SumsIncomeAndExpenseSeparately()
    {
        // Arrange: the Wildberries sale row of the sample report — revenue, a commission charge,
        // an acquiring compensation (positive) and the delivery row of the same unit.
        List<PnlEntry> entries = new()
        {
            Entry(PnlEntryType.Sale, 204m),
            Entry(PnlEntryType.Commission, 15.33m, PnlEntryDirection.Expense),
            Entry(PnlEntryType.Acquiring, 8.16m),
            Entry(PnlEntryType.Logistics, 53.84m, PnlEntryDirection.Expense),
        };

        // Act
        PnlLineTotals totals = PnlLineAggregator.Calculate(entries);

        // Assert
        totals.RevenueAmount.Should().Be(204m);
        totals.CommissionAmount.Should().Be(-15.33m);
        totals.AcquiringAmount.Should().Be(8.16m);
        totals.DeliveryFeeAmount.Should().Be(-53.84m);
        totals.RefundAmount.Should().Be(0m);
        totals.NetRevenueAmount.Should().Be(142.99m);
        totals.EntryCount.Should().Be(4);
        totals.ControlTotalAmount.Should().BeNull();
        totals.ControlDifferenceAmount.Should().BeNull();
    }

    [Fact]
    public void Calculate_SupersededEntry_IsExcludedFromTotals()
    {
        // Arrange: a re-issued report corrected the sale from 100 to 90; both versions are stored.
        Guid newerEntryId = Guid.NewGuid();

        List<PnlEntry> entries = new()
        {
            Entry(PnlEntryType.Sale, 100m, supersededBy: newerEntryId),
            Entry(PnlEntryType.Sale, 90m, id: newerEntryId),
        };

        // Act
        PnlLineTotals totals = PnlLineAggregator.Calculate(entries);

        // Assert
        totals.RevenueAmount.Should().Be(90m);
        totals.NetRevenueAmount.Should().Be(90m);
        totals.EntryCount.Should().Be(1);
    }

    [Fact]
    public void Calculate_ControlTotal_IsComparedButNotSummed()
    {
        // Arrange: the channel reports 188.67 remitted for a 204 sale with a 15.33 commission.
        List<PnlEntry> entries = new()
        {
            Entry(PnlEntryType.Sale, 204m),
            Entry(PnlEntryType.Commission, 15.33m, PnlEntryDirection.Expense),
            Entry(PnlEntryType.Payout, 188.67m, kind: PnlEntryKind.ControlTotal),
        };

        // Act
        PnlLineTotals totals = PnlLineAggregator.Calculate(entries);

        // Assert: the payout is a check, not an addend.
        totals.NetRevenueAmount.Should().Be(188.67m);
        totals.ControlTotalAmount.Should().Be(188.67m);
        totals.ControlDifferenceAmount.Should().Be(0m);
        totals.EntryCount.Should().Be(2);
    }

    [Fact]
    public void Calculate_ComponentsDisagreeWithControlTotal_ReportsNonZeroDifference()
    {
        // Arrange: a component was mapped from the wrong field, so the parts do not explain the
        // payout. The difference is the only signal that something is missing.
        List<PnlEntry> entries = new()
        {
            Entry(PnlEntryType.Sale, 204m),
            Entry(PnlEntryType.Payout, 214.54m, kind: PnlEntryKind.ControlTotal),
        };

        // Act
        PnlLineTotals totals = PnlLineAggregator.Calculate(entries);

        // Assert
        totals.NetRevenueAmount.Should().Be(204m);
        totals.ControlTotalAmount.Should().Be(214.54m);
        totals.ControlDifferenceAmount.Should().Be(-10.54m);
    }

    [Fact]
    public void Calculate_NoEntries_ReturnsEmptyTotals()
    {
        // Act
        PnlLineTotals totals = PnlLineAggregator.Calculate(Array.Empty<PnlEntry>());

        // Assert
        totals.NetRevenueAmount.Should().Be(0m);
        totals.EntryCount.Should().Be(0);
        totals.ControlTotalAmount.Should().BeNull();
    }

    [Fact]
    public void Calculate_NegativeAmount_Throws()
    {
        // Arrange: the sign belongs in Direction; a negative amount means the writer did not
        // normalise the channel payload.
        List<PnlEntry> entries = new()
        {
            Entry(PnlEntryType.Commission, -15.33m, PnlEntryDirection.Expense),
        };

        // Act
        Action act = () => PnlLineAggregator.Calculate(entries);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Calculate_PayoutAsComponent_Throws()
    {
        // Arrange: a payout mixed into the components would count the commission and the delivery
        // twice.
        List<PnlEntry> entries = new()
        {
            Entry(PnlEntryType.Payout, 188.67m),
        };

        // Act
        Action act = () => PnlLineAggregator.Calculate(entries);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Calculate_ControlTotalOfComponentType_Throws()
    {
        // Arrange: a control total must be typed as a payout.
        List<PnlEntry> entries = new()
        {
            Entry(PnlEntryType.Sale, 188.67m, kind: PnlEntryKind.ControlTotal),
        };

        // Act
        Action act = () => PnlLineAggregator.Calculate(entries);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    private static PnlEntry Entry(
        PnlEntryType type,
        decimal amount,
        PnlEntryDirection direction = PnlEntryDirection.Income,
        PnlEntryKind kind = PnlEntryKind.Component,
        Guid? supersededBy = null,
        Guid? id = null)
    {
        return new PnlEntry
        {
            Id = id ?? Guid.NewGuid(),
            Source = PnlSource.WbReport,
            ReportInstanceKey = "799692300:2026-08-03",
            ExternalEntryId = "3131711029843",
            ChannelId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ExternalOrderId = null,
            ExternalLineId = "eBl.rbcae8cd5eb1f4ed7848aed79801a9d95.0.0",
            ExternalProductId = "174371959",
            Kind = kind,
            Type = type,
            Amount = amount,
            Direction = direction,
            Currency = "RUB",
            DocumentDate = new DateOnly(2026, 8, 1),
            PeriodFrom = new DateOnly(2026, 7, 27),
            PeriodTo = new DateOnly(2026, 8, 2),
            ImportedAt = new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero),
            CreatedAt = new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero),
            SupersededByEntryId = supersededBy,
        };
    }
}
