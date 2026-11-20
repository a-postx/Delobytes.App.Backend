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
/// tests pin the rules the totals depend on: the sign comes from the direction rather than from the
/// amount, a superseded version does not count, a control total is a check rather than an addend,
/// and a component that was never reported stays null rather than becoming zero.
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
        totals.RefundAmount.Should().BeNull();
        totals.CompensationAmount.Should().BeNull();
        totals.NetRevenueAmount.Should().Be(142.99m);
        totals.EntryCount.Should().Be(4);
        totals.ControlTotalAmount.Should().BeNull();
        totals.ControlDifferenceAmount.Should().BeNull();
        totals.HasSale.Should().BeTrue();
        totals.SaleDate.Should().Be(new DateOnly(2026, 8, 1));
    }

    [Fact]
    public void Calculate_Compensation_IsSummedIntoNetRevenue()
    {
        // Arrange: the Ozon posting of the sample report — sale, marketplace-funded bonus and
        // coinvestment, commission and logistics.
        List<PnlEntry> entries = new()
        {
            Entry(PnlEntryType.Sale, 920.38m),
            Entry(PnlEntryType.Compensation, 663.62m),
            Entry(PnlEntryType.Commission, 823.68m, PnlEntryDirection.Expense),
            Entry(PnlEntryType.Logistics, 159.1m, PnlEntryDirection.Expense),
        };

        // Act
        PnlLineTotals totals = PnlLineAggregator.Calculate(entries);

        // Assert: the compensation is revenue of its own, and the components reconcile to the
        // control total of 601.22 once it is included.
        totals.CompensationAmount.Should().Be(663.62m);
        totals.NetRevenueAmount.Should().Be(601.22m);
        totals.EntryCount.Should().Be(4);
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
        totals.HasSale.Should().BeTrue();
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

        // Assert: nothing reported is not the same as every component being zero.
        totals.NetRevenueAmount.Should().BeNull();
        totals.RevenueAmount.Should().BeNull();
        totals.CommissionAmount.Should().BeNull();
        totals.AcquiringAmount.Should().BeNull();
        totals.DeliveryFeeAmount.Should().BeNull();
        totals.RefundAmount.Should().BeNull();
        totals.CompensationAmount.Should().BeNull();
        totals.EntryCount.Should().Be(0);
        totals.ControlTotalAmount.Should().BeNull();
        totals.HasSale.Should().BeFalse();
        totals.SaleDate.Should().BeNull();
    }

    [Fact]
    public void Calculate_ExplicitZeroComponent_IsZeroNotNull()
    {
        // Arrange: the channel reported a zero acquiring fee, which is a fact, unlike a missing one.
        List<PnlEntry> entries = new()
        {
            Entry(PnlEntryType.Sale, 100m),
            Entry(PnlEntryType.Acquiring, 0m, PnlEntryDirection.Expense),
        };

        // Act
        PnlLineTotals totals = PnlLineAggregator.Calculate(entries);

        // Assert
        totals.AcquiringAmount.Should().Be(0m);
        totals.CompensationAmount.Should().BeNull();
        totals.RefundAmount.Should().BeNull();
        totals.NetRevenueAmount.Should().Be(100m);
    }

    [Fact]
    public void Calculate_OnlyNonSaleComponents_ReportsNoSale()
    {
        // Arrange: a cancellation leaves only a logistics charge; no sale happened, so no cost of
        // goods may be applied by a later projection.
        List<PnlEntry> entries = new()
        {
            Entry(PnlEntryType.Logistics, 53.84m, PnlEntryDirection.Expense),
        };

        // Act
        PnlLineTotals totals = PnlLineAggregator.Calculate(entries);

        // Assert
        totals.HasSale.Should().BeFalse();
        totals.SaleDate.Should().BeNull();
        totals.RevenueAmount.Should().BeNull();
        totals.NetRevenueAmount.Should().Be(-53.84m);
    }

    [Fact]
    public void Calculate_LogisticsOnlyLine_CarriesTheDatesOfItsDocuments()
    {
        // Arrange: a non-buyout reports the delivery charge and nothing else. There is no sale date,
        // but the charge was booked on a day and the line still has to belong to a period.
        List<PnlEntry> entries = new()
        {
            Entry(PnlEntryType.Logistics, 53.84m, PnlEntryDirection.Expense, documentDate: new DateOnly(2026, 8, 4)),
        };

        // Act
        PnlLineTotals totals = PnlLineAggregator.Calculate(entries);

        // Assert
        totals.SaleDate.Should().BeNull();
        totals.PeriodFrom.Should().Be(new DateOnly(2026, 8, 4));
        totals.PeriodTo.Should().Be(new DateOnly(2026, 8, 4));
    }

    [Fact]
    public void Calculate_ComponentsOnSeveralDays_SpansTheEarliestAndTheLatestDocument()
    {
        // Arrange: the sale and the delivery of the same unit arrive as two documents of one report.
        List<PnlEntry> entries = new()
        {
            Entry(PnlEntryType.Sale, 204m, documentDate: new DateOnly(2026, 8, 1)),
            Entry(PnlEntryType.Logistics, 53.84m, PnlEntryDirection.Expense, documentDate: new DateOnly(2026, 8, 3)),
        };

        // Act
        PnlLineTotals totals = PnlLineAggregator.Calculate(entries);

        // Assert: the sale date stays the sale's own document date, unaffected by the later delivery.
        totals.SaleDate.Should().Be(new DateOnly(2026, 8, 1));
        totals.PeriodFrom.Should().Be(new DateOnly(2026, 8, 1));
        totals.PeriodTo.Should().Be(new DateOnly(2026, 8, 3));
    }

    [Fact]
    public void Calculate_NoEntries_HasNoPeriodDates()
    {
        // Act
        PnlLineTotals totals = PnlLineAggregator.Calculate(Array.Empty<PnlEntry>());

        // Assert
        totals.PeriodFrom.Should().BeNull();
        totals.PeriodTo.Should().BeNull();
    }

    [Fact]
    public void Calculate_SeveralSaleEntries_TakesEarliestDocumentDate()
    {
        // Arrange
        List<PnlEntry> entries = new()
        {
            Entry(PnlEntryType.Sale, 100m, documentDate: new DateOnly(2026, 8, 5)),
            Entry(PnlEntryType.Sale, 50m, documentDate: new DateOnly(2026, 8, 2)),
        };

        // Act
        PnlLineTotals totals = PnlLineAggregator.Calculate(entries);

        // Assert
        totals.SaleDate.Should().Be(new DateOnly(2026, 8, 2));
        totals.RevenueAmount.Should().Be(150m);
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

    [Fact]
    public void Calculate_SaleAndItsReversal_NetRevenueIsZero()
    {
        // Arrange: a unit sold and then reversed. The reversal is booked as a sale with the opposite
        // direction, so it offsets the revenue. HasSale stays true because a sale did happen in this
        // line; netting the units by quantity so that a fully reversed sale stops claiming cost of
        // goods is a separate, quantity-aware concern.
        List<PnlEntry> entries = new()
        {
            Entry(PnlEntryType.Sale, 204m, documentDate: new DateOnly(2026, 8, 1)),
            Entry(PnlEntryType.Sale, 204m, PnlEntryDirection.Expense, documentDate: new DateOnly(2026, 8, 2)),
        };

        // Act
        PnlLineTotals totals = PnlLineAggregator.Calculate(entries);

        // Assert
        totals.NetRevenueAmount.Should().Be(0m);
        totals.RevenueAmount.Should().Be(0m);
        totals.EntryCount.Should().Be(2);
        totals.HasSale.Should().BeTrue();
    }

    [Fact]
    public void Calculate_ReversalOnly_ReportsNoSale()
    {
        // Arrange: the sale itself belongs to an earlier report; only its reversal is in this one.
        List<PnlEntry> entries = new()
        {
            Entry(PnlEntryType.Sale, 204m, PnlEntryDirection.Expense),
        };

        // Act
        PnlLineTotals totals = PnlLineAggregator.Calculate(entries);

        // Assert
        totals.NetRevenueAmount.Should().Be(-204m);
        totals.HasSale.Should().BeFalse();
    }

    private static PnlEntry Entry(
        PnlEntryType type,
        decimal amount,
        PnlEntryDirection direction = PnlEntryDirection.Income,
        PnlEntryKind kind = PnlEntryKind.Component,
        Guid? supersededBy = null,
        Guid? id = null,
        DateOnly? documentDate = null)
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
            DocumentDate = documentDate ?? new DateOnly(2026, 8, 1),
            PeriodFrom = new DateOnly(2026, 7, 27),
            PeriodTo = new DateOnly(2026, 8, 2),
            ImportedAt = new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero),
            CreatedAt = new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero),
            SupersededByEntryId = supersededBy,
        };
    }
}
