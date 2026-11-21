using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Finance.Application.Models;
using Delobytes.App.Backend.Finance.Application.Projection;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Domain.Enums;
using Delobytes.App.Backend.Tests.Application.Finance.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Finance;

/// <summary>
/// Tests for PnlLineProjector. The projection is derived from two facts that arrive independently,
/// so these tests pin both orders: money before the order line and the order line before money, plus
/// the rule that cost of goods never applies without a sale.
/// </summary>
public class PnlLineProjectorTests
{
    private static readonly Guid ChannelId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecomputeAsync_EntriesArrivedBeforeTheRef_CalculatesTheLine()
    {
        // Arrange: the journal was written first, the order line projection arrived later.
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        journal.Entries.Add(SaleEntry(204m));

        InMemoryOrderLineRefStore refs = new InMemoryOrderLineRefStore();
        OrderLineRef line = Ref(Guid.NewGuid());
        refs.Refs[line.OrderLineId] = line;

        InMemoryPnlLineStore lines = new InMemoryPnlLineStore();
        PnlLineProjector projector = CreateProjector(journal, refs, new InMemoryOrderLineCostStore(), lines);

        // Act
        await projector.RecomputeAsync(new[] { line.OrderLineId }, CancellationToken.None);

        // Assert
        PnlLine stored = SingleLine(lines, line.OrderLineId);
        stored.State.Should().Be(PnlLineState.Calculated);
        stored.RevenueAmount.Should().Be(204m);
        stored.RevenueSource.Should().Be(ValueSource.ChannelFinanceReport);
        stored.NetRevenueSource.Should().Be(ValueSource.Derived);
        stored.EntryCount.Should().Be(1);
        stored.SaleDate.Should().Be(new DateOnly(2026, 8, 1));
        stored.Currency.Should().Be("RUB");
        stored.ComputedAt.Should().Be(Now);
    }

    [Fact]
    public async Task RecomputeAsync_NoEntries_LeavesTheLineNotCalculated()
    {
        // Arrange: the order line is registered and no money has arrived yet.
        InMemoryOrderLineRefStore refs = new InMemoryOrderLineRefStore();
        OrderLineRef line = Ref(Guid.NewGuid());
        refs.Refs[line.OrderLineId] = line;

        InMemoryPnlLineStore lines = new InMemoryPnlLineStore();

        PnlLineProjector projector = CreateProjector(
            new InMemoryPnlJournalStore(),
            refs,
            new InMemoryOrderLineCostStore(),
            lines);

        // Act
        await projector.RecomputeAsync(new[] { line.OrderLineId }, CancellationToken.None);

        // Assert
        PnlLine stored = SingleLine(lines, line.OrderLineId);
        stored.State.Should().Be(PnlLineState.NotCalculated);
        stored.RevenueAmount.Should().BeNull();
        stored.NetRevenueAmount.Should().BeNull();
        stored.EntryCount.Should().Be(0);
        stored.SaleDate.Should().BeNull();
    }

    [Fact]
    public async Task RecomputeForKeysAsync_CostArrivedBeforeTheRef_IsAppliedOnceTheLineIsKnown()
    {
        // Arrange: the cost is stored against a line the projection has not seen yet.
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        journal.Entries.Add(SaleEntry(204m));

        InMemoryOrderLineRefStore refs = new InMemoryOrderLineRefStore();
        OrderLineRef line = Ref(Guid.NewGuid());
        refs.Refs[line.OrderLineId] = line;

        InMemoryOrderLineCostStore costs = new InMemoryOrderLineCostStore();
        costs.Costs[line.OrderLineId] = ResolvedCost(line.OrderLineId, 100m);

        InMemoryPnlLineStore lines = new InMemoryPnlLineStore();
        PnlLineProjector projector = CreateProjector(journal, refs, costs, lines);

        // Act
        await projector.RecomputeForKeysAsync(
            new[] { OrderLineAttributionKey.ForLine(ChannelId, line.ExternalLineId!) },
            CancellationToken.None);

        // Assert
        PnlLine stored = SingleLine(lines, line.OrderLineId);
        stored.ProductionCostAmount.Should().Be(100m);
        stored.ProductionCostIsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task RecomputeAsync_ProductNotLinkedCost_DoesNotApplyCostOfGoods()
    {
        // Arrange
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        journal.Entries.Add(SaleEntry(204m));

        InMemoryOrderLineRefStore refs = new InMemoryOrderLineRefStore();
        OrderLineRef line = Ref(Guid.NewGuid());
        refs.Refs[line.OrderLineId] = line;

        InMemoryOrderLineCostStore costs = new InMemoryOrderLineCostStore();

        OrderLineCost cost = ResolvedCost(line.OrderLineId, 100m);
        cost.Status = OrderLineCostStatus.ProductNotLinked;
        cost.ProductId = null;
        cost.TotalCost = null;
        costs.Costs[line.OrderLineId] = cost;

        InMemoryPnlLineStore lines = new InMemoryPnlLineStore();
        PnlLineProjector projector = CreateProjector(journal, refs, costs, lines);

        // Act
        await projector.RecomputeAsync(new[] { line.OrderLineId }, CancellationToken.None);

        // Assert
        PnlLine stored = SingleLine(lines, line.OrderLineId);
        stored.ProductionCostAmount.Should().BeNull();
        stored.ProductionCostIsComplete.Should().BeNull();
    }

    [Fact]
    public async Task RecomputeAsync_LineWithoutASale_KeepsItsResultWithoutCostOfGoods()
    {
        // Arrange: a non-buyout leaves only the logistics charge.
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        journal.Entries.Add(LogisticsEntry(53.84m));

        InMemoryOrderLineRefStore refs = new InMemoryOrderLineRefStore();
        OrderLineRef line = Ref(Guid.NewGuid());
        refs.Refs[line.OrderLineId] = line;

        InMemoryOrderLineCostStore costs = new InMemoryOrderLineCostStore();
        costs.Costs[line.OrderLineId] = ResolvedCost(line.OrderLineId, 100m);

        InMemoryPnlLineStore lines = new InMemoryPnlLineStore();
        PnlLineProjector projector = CreateProjector(journal, refs, costs, lines);

        // Act
        await projector.RecomputeAsync(new[] { line.OrderLineId }, CancellationToken.None);

        // Assert
        PnlLine stored = SingleLine(lines, line.OrderLineId);
        stored.State.Should().Be(PnlLineState.Calculated);
        stored.NetRevenueAmount.Should().Be(-53.84m);
        stored.ProductionCostAmount.Should().BeNull();
    }

    [Fact]
    public async Task RecomputeAsync_ReturnedUnit_KeepsNoCostOfGoods()
    {
        // Arrange: the unit sold and came back, so its sale and its return net to nothing.
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        journal.Entries.Add(SaleEntry(350.99m));
        journal.Entries.Add(ReturnEntry(350.99m));

        InMemoryOrderLineRefStore refs = new InMemoryOrderLineRefStore();
        OrderLineRef line = Ref(Guid.NewGuid());
        refs.Refs[line.OrderLineId] = line;

        InMemoryOrderLineCostStore costs = new InMemoryOrderLineCostStore();
        costs.Costs[line.OrderLineId] = ResolvedCost(line.OrderLineId, 100m);

        InMemoryPnlLineStore lines = new InMemoryPnlLineStore();
        PnlLineProjector projector = CreateProjector(journal, refs, costs, lines);

        // Act
        await projector.RecomputeAsync(new[] { line.OrderLineId }, CancellationToken.None);

        // Assert: the goods are back with the seller, so nothing was consumed.
        PnlLine stored = SingleLine(lines, line.OrderLineId);
        stored.NetRevenueAmount.Should().Be(0m);
        stored.ReturnedQuantity.Should().Be(1);
        stored.ProductionCostAmount.Should().BeNull();
        stored.ProductionCostIsComplete.Should().BeNull();
    }

    [Fact]
    public async Task RecomputeAsync_PartiallyReturnedLine_ChargesOnlyTheUnitsThatStayedSold()
    {
        // Arrange: three units ordered, one of them returned.
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        journal.Entries.Add(SaleEntry(900m));
        journal.Entries.Add(ReturnEntry(300m));

        InMemoryOrderLineRefStore refs = new InMemoryOrderLineRefStore();
        OrderLineRef line = Ref(Guid.NewGuid(), quantity: 3);
        refs.Refs[line.OrderLineId] = line;

        InMemoryOrderLineCostStore costs = new InMemoryOrderLineCostStore();
        costs.Costs[line.OrderLineId] = ResolvedCost(line.OrderLineId, 100m, quantity: 3);

        InMemoryPnlLineStore lines = new InMemoryPnlLineStore();
        PnlLineProjector projector = CreateProjector(journal, refs, costs, lines);

        // Act
        await projector.RecomputeAsync(new[] { line.OrderLineId }, CancellationToken.None);

        // Assert: two units stayed sold at 100 each, the returned one carries no cost.
        PnlLine stored = SingleLine(lines, line.OrderLineId);
        stored.NetRevenueAmount.Should().Be(600m);
        stored.ReturnedQuantity.Should().Be(1);
        stored.ProductionCostAmount.Should().Be(200m);
    }

    [Fact]
    public async Task RecomputeAsync_SecondPassWithEntries_MovesTheLineToCalculated()
    {
        // Arrange: the first recompute runs before any money arrives.
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        InMemoryOrderLineRefStore refs = new InMemoryOrderLineRefStore();
        OrderLineRef line = Ref(Guid.NewGuid());
        refs.Refs[line.OrderLineId] = line;

        InMemoryPnlLineStore lines = new InMemoryPnlLineStore();
        PnlLineProjector projector = CreateProjector(journal, refs, new InMemoryOrderLineCostStore(), lines);

        await projector.RecomputeAsync(new[] { line.OrderLineId }, CancellationToken.None);

        SingleLine(lines, line.OrderLineId).State.Should().Be(PnlLineState.NotCalculated);

        // Act
        journal.Entries.Add(SaleEntry(204m));
        await projector.RecomputeAsync(new[] { line.OrderLineId }, CancellationToken.None);

        // Assert
        SingleLine(lines, line.OrderLineId).State.Should().Be(PnlLineState.Calculated);
        SingleLine(lines, line.OrderLineId).RevenueAmount.Should().Be(204m);
    }

    [Fact]
    public async Task RecomputeAsync_OneLineFailsCalculation_OtherLinesStillRecompute()
    {
        // Arrange: the bad line's journal entry carries a negative amount, which
        // PnlLineAggregator.Calculate rejects. The good line must still get its result.
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        journal.Entries.Add(SaleEntry(204m, externalLineId: "srid-good", externalEntryId: "rrd-good"));

        PnlEntry badEntry = SaleEntry(-10m, externalLineId: "srid-bad", externalEntryId: "rrd-bad");
        journal.Entries.Add(badEntry);

        InMemoryOrderLineRefStore refs = new InMemoryOrderLineRefStore();
        OrderLineRef goodLine = Ref(Guid.NewGuid(), "srid-good");
        OrderLineRef badLine = Ref(Guid.NewGuid(), "srid-bad");
        refs.Refs[goodLine.OrderLineId] = goodLine;
        refs.Refs[badLine.OrderLineId] = badLine;

        InMemoryPnlLineStore lines = new InMemoryPnlLineStore();
        PnlLineProjector projector = CreateProjector(journal, refs, new InMemoryOrderLineCostStore(), lines);

        // Act
        PnlRecomputeResult result = await projector.RecomputeAsync(
            new[] { goodLine.OrderLineId, badLine.OrderLineId }, CancellationToken.None);

        // Assert
        result.Succeeded.Should().Be(1);
        result.Failed.Should().Be(1);
        result.FailedOrderLineIds.Should().ContainSingle().Which.Should().Be(badLine.OrderLineId);

        SingleLine(lines, goodLine.OrderLineId).RevenueAmount.Should().Be(204m);
        lines.Lines.Should().NotContainKey(badLine.OrderLineId, "a line that never finished its calculation must not be staged");
    }

    [Fact]
    public async Task RecomputeAsync_ExistingLineFailsCalculation_LeavesItsStoredResultUnchanged()
    {
        // Arrange: the line already has a good result from an earlier pass.
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        journal.Entries.Add(SaleEntry(204m));

        InMemoryOrderLineRefStore refs = new InMemoryOrderLineRefStore();
        OrderLineRef line = Ref(Guid.NewGuid());
        refs.Refs[line.OrderLineId] = line;

        InMemoryPnlLineStore lines = new InMemoryPnlLineStore();
        PnlLineProjector projector = CreateProjector(journal, refs, new InMemoryOrderLineCostStore(), lines);

        await projector.RecomputeAsync(new[] { line.OrderLineId }, CancellationToken.None);
        SingleLine(lines, line.OrderLineId).RevenueAmount.Should().Be(204m);

        // A later arrival corrupts the journal for this line: the stored result must survive.
        journal.Entries.Clear();
        journal.Entries.Add(SaleEntry(-10m));

        // Act
        PnlRecomputeResult result = await projector.RecomputeAsync(new[] { line.OrderLineId }, CancellationToken.None);

        // Assert
        result.Succeeded.Should().Be(0);
        result.Failed.Should().Be(1);
        result.FailedOrderLineIds.Should().ContainSingle().Which.Should().Be(line.OrderLineId);

        SingleLine(lines, line.OrderLineId).RevenueAmount.Should().Be(204m);
    }

    [Fact]
    public async Task RecomputeAsync_WbSaleWithoutLogistics_CreatesAnExpectationAndMarksTheLineProvisional()
    {
        // Arrange: a Wildberries sale always has a separate delivery row, and it has not arrived.
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        journal.Entries.Add(SaleEntry(204m));

        InMemoryOrderLineRefStore refs = new InMemoryOrderLineRefStore();
        OrderLineRef line = Ref(Guid.NewGuid());
        refs.Refs[line.OrderLineId] = line;

        InMemoryPnlExpectationStore expectations = new InMemoryPnlExpectationStore();
        InMemoryPnlLineStore lines = new InMemoryPnlLineStore();
        PnlLineProjector projector = CreateProjector(
            journal, refs, new InMemoryOrderLineCostStore(), lines, expectations);

        // Act
        await projector.RecomputeAsync(new[] { line.OrderLineId }, CancellationToken.None);

        // Assert: the deadline is anchored to the reported period end plus the fixed window.
        PnlLineExpectation expectation = expectations.Expectations.Should().ContainSingle().Subject;
        expectation.ExpectedType.Should().Be(PnlEntryType.Logistics);
        expectation.State.Should().Be(PnlExpectationState.Expected);
        expectation.ExpectedBy.Should().Be(new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero));

        PnlLine stored = SingleLine(lines, line.OrderLineId);
        stored.CompletenessState.Should().Be(PnlLineCompleteness.Provisional);
        stored.MissingDocumentTypes.Should().ContainSingle().Which.Should().Be(nameof(PnlEntryType.Logistics));
        stored.ExpectedBy.Should().Be(expectation.ExpectedBy);
        stored.RevenueAmount.Should().Be(204m);
    }

    [Fact]
    public async Task RecomputeAsync_WbSaleWithLogistics_LeavesTheLineComplete()
    {
        // Arrange: both rows of the unit arrived in the same batch.
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        journal.Entries.Add(SaleEntry(204m));
        journal.Entries.Add(LogisticsEntry(53.84m));

        InMemoryOrderLineRefStore refs = new InMemoryOrderLineRefStore();
        OrderLineRef line = Ref(Guid.NewGuid());
        refs.Refs[line.OrderLineId] = line;

        InMemoryPnlExpectationStore expectations = new InMemoryPnlExpectationStore();
        InMemoryPnlLineStore lines = new InMemoryPnlLineStore();
        PnlLineProjector projector = CreateProjector(
            journal, refs, new InMemoryOrderLineCostStore(), lines, expectations);

        // Act
        await projector.RecomputeAsync(new[] { line.OrderLineId }, CancellationToken.None);

        // Assert
        expectations.Expectations.Should().BeEmpty();

        PnlLine stored = SingleLine(lines, line.OrderLineId);
        stored.CompletenessState.Should().Be(PnlLineCompleteness.Complete);
        stored.MissingDocumentTypes.Should().BeEmpty();
        stored.ExpectedBy.Should().BeNull();
        stored.DeliveryFeeAmount.Should().Be(-53.84m);
    }

    [Fact]
    public async Task RecomputeAsync_LogisticsArrivesLater_ClosesTheExpectation()
    {
        // Arrange
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        journal.Entries.Add(SaleEntry(204m));

        InMemoryOrderLineRefStore refs = new InMemoryOrderLineRefStore();
        OrderLineRef line = Ref(Guid.NewGuid());
        refs.Refs[line.OrderLineId] = line;

        InMemoryPnlExpectationStore expectations = new InMemoryPnlExpectationStore();
        InMemoryPnlLineStore lines = new InMemoryPnlLineStore();
        PnlLineProjector projector = CreateProjector(
            journal, refs, new InMemoryOrderLineCostStore(), lines, expectations);

        await projector.RecomputeAsync(new[] { line.OrderLineId }, CancellationToken.None);
        expectations.Expectations.Should().ContainSingle()
            .Which.State.Should().Be(PnlExpectationState.Expected);

        // Act: the delivery row of the same unit arrives in a later report.
        journal.Entries.Add(LogisticsEntry(53.84m));
        await projector.RecomputeAsync(new[] { line.OrderLineId }, CancellationToken.None);

        // Assert
        PnlLineExpectation expectation = expectations.Expectations.Should().ContainSingle().Subject;
        expectation.State.Should().Be(PnlExpectationState.ClosedByDocument);
        expectation.ClosedAt.Should().Be(Now);

        PnlLine stored = SingleLine(lines, line.OrderLineId);
        stored.CompletenessState.Should().Be(PnlLineCompleteness.Complete);
        stored.MissingDocumentTypes.Should().BeEmpty();
    }

    [Fact]
    public async Task RecomputeAsync_ReversalOnly_ExpectsNoLogisticsAndAppliesNoCost()
    {
        // Arrange: the sale was reversed, so only its reversal sits in the journal. No unit left the
        // seller: the line owes no delivery document and no cost of goods.
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        PnlEntry reversal = SaleEntry(204m);
        reversal.Direction = PnlEntryDirection.Expense;
        journal.Entries.Add(reversal);

        InMemoryOrderLineRefStore refs = new InMemoryOrderLineRefStore();
        OrderLineRef line = Ref(Guid.NewGuid());
        refs.Refs[line.OrderLineId] = line;

        InMemoryOrderLineCostStore costs = new InMemoryOrderLineCostStore();
        costs.Costs[line.OrderLineId] = ResolvedCost(line.OrderLineId, 100m);

        InMemoryPnlExpectationStore expectations = new InMemoryPnlExpectationStore();
        InMemoryPnlLineStore lines = new InMemoryPnlLineStore();
        PnlLineProjector projector = CreateProjector(journal, refs, costs, lines, expectations);

        // Act
        await projector.RecomputeAsync(new[] { line.OrderLineId }, CancellationToken.None);

        // Assert
        expectations.Expectations.Should().BeEmpty();

        PnlLine stored = SingleLine(lines, line.OrderLineId);
        stored.CompletenessState.Should().Be(PnlLineCompleteness.Complete);
        stored.MissingDocumentTypes.Should().BeEmpty();
        stored.NetRevenueAmount.Should().Be(-204m);
        stored.ProductionCostAmount.Should().BeNull();
    }

    [Fact]
    public async Task RecomputeAsync_LogisticsOnlyLine_WritesThePeriodOfItsDocuments()
    {
        // Arrange: a non-buyout arrives as a delivery charge without a sale. The line has no sale date
        // and still has to belong to a period, otherwise no from/to query and no summary can see it.
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        journal.Entries.Add(LogisticsEntry(53.84m));

        InMemoryOrderLineRefStore refs = new InMemoryOrderLineRefStore();
        OrderLineRef line = Ref(Guid.NewGuid());
        refs.Refs[line.OrderLineId] = line;

        InMemoryPnlLineStore lines = new InMemoryPnlLineStore();
        PnlLineProjector projector = CreateProjector(journal, refs, new InMemoryOrderLineCostStore(), lines);

        // Act
        await projector.RecomputeAsync(new[] { line.OrderLineId }, CancellationToken.None);

        // Assert
        PnlLine stored = SingleLine(lines, line.OrderLineId);
        stored.SaleDate.Should().BeNull();
        stored.FinancialPeriodFrom.Should().Be(new DateOnly(2026, 8, 1));
        stored.FinancialPeriodTo.Should().Be(new DateOnly(2026, 8, 1));
        stored.NetRevenueAmount.Should().Be(-53.84m);
        stored.ProductionCostAmount.Should().BeNull();
    }

    private static PnlLineProjector CreateProjector(
        InMemoryPnlJournalStore journal,
        InMemoryOrderLineRefStore refs,
        InMemoryOrderLineCostStore costs,
        InMemoryPnlLineStore lines,
        InMemoryPnlExpectationStore? expectations = null)
    {
        return new PnlLineProjector(
            journal,
            refs,
            costs,
            expectations ?? new InMemoryPnlExpectationStore(),
            lines,
            new FixedTimeProvider(Now),
            NullLogger<PnlLineProjector>.Instance);
    }

    private static PnlLine SingleLine(InMemoryPnlLineStore lines, Guid orderLineId)
    {
        lines.Lines.Should().ContainKey(orderLineId);

        return lines.Lines[orderLineId];
    }

    private static OrderLineRef Ref(Guid orderLineId, string externalLineId = "srid-1", int quantity = 1)
    {
        return new OrderLineRef
        {
            OrderLineId = orderLineId,
            OrderId = Guid.NewGuid(),
            ChannelId = ChannelId,
            ExternalOrderId = "order-1",
            ExternalLineId = externalLineId,
            ExternalProductId = "174371959",
            Quantity = quantity,
            OrderDate = Now,
            Currency = "RUB",
            UpdatedAt = Now,
        };
    }

    private static PnlEntry SaleEntry(decimal amount, string externalLineId = "srid-1", string externalEntryId = "rrd-1")
    {
        return new PnlEntry
        {
            Id = Guid.NewGuid(),
            Source = PnlSource.WbReport,
            ReportInstanceKey = "799692300:2026-08-03",
            ExternalEntryId = externalEntryId,
            ChannelId = ChannelId,
            ExternalOrderId = null,
            ExternalLineId = externalLineId,
            ExternalProductId = "174371959",
            Kind = PnlEntryKind.Component,
            Type = PnlEntryType.Sale,
            Amount = amount,
            Direction = PnlEntryDirection.Income,
            Currency = "RUB",
            DocumentDate = new DateOnly(2026, 8, 1),
            PeriodFrom = new DateOnly(2026, 7, 27),
            PeriodTo = new DateOnly(2026, 8, 2),
        };
    }

    private static PnlEntry LogisticsEntry(decimal amount)
    {
        PnlEntry entry = SaleEntry(amount);
        entry.ExternalEntryId = "rrd-2";
        entry.Type = PnlEntryType.Logistics;
        entry.Direction = PnlEntryDirection.Expense;

        return entry;
    }

    private static PnlEntry ReturnEntry(decimal amount)
    {
        PnlEntry entry = SaleEntry(amount);
        entry.ExternalEntryId = "rrd-3";
        entry.Direction = PnlEntryDirection.Expense;

        return entry;
    }

    private static OrderLineCost ResolvedCost(Guid orderLineId, decimal unitCost, int quantity = 1)
    {
        return new OrderLineCost
        {
            OrderLineId = orderLineId,
            Status = OrderLineCostStatus.Resolved,
            ProductId = Guid.NewGuid(),
            AsOfDate = new DateOnly(2026, 8, 1),
            Quantity = quantity,
            UnitTotalCost = unitCost,
            TotalCost = unitCost * quantity,
            IsComplete = true,
            ResolvedAt = Now,
            CreatedAt = Now,
        };
    }
}
