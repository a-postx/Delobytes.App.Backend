using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.ProductTable;
using Delobytes.App.Backend.Finance.Application.Queries;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using Delobytes.App.Backend.Finance.Domain.Enums;
using Delobytes.App.Backend.Tests.Application.Finance.Fakes;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Finance;

/// <summary>
/// The document pass of the product sheet: the pending and overdue groups never overlap, a type is
/// counted once per line, and the figures reconcile with the product row.
/// </summary>
public class PnlDocumentGapAggregatorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Aggregate_SplitsPendingAndOverdueAndSumsTheirRevenue()
    {
        PnlLineRow firstPending = Line(
            revenue: 100m,
            completeness: PnlLineCompleteness.Provisional,
            types: new[] { "Commission" });
        PnlLineRow secondPending = Line(
            revenue: 200m,
            completeness: PnlLineCompleteness.Provisional,
            types: new[] { "Commission", "Acquiring" });
        PnlLineRow overdue = Line(
            revenue: 300m,
            completeness: PnlLineCompleteness.Overdue,
            types: new[] { "Acquiring" });

        PnlProductDocumentGaps gaps = await AggregateAsync(firstPending, secondPending, overdue);

        gaps.Pending.Lines.Should().Be(2);
        gaps.Pending.RevenueAmount.Should().Be(300m);
        gaps.Overdue.Lines.Should().Be(1);
        gaps.Overdue.RevenueAmount.Should().Be(300m);
    }

    [Fact]
    public async Task Aggregate_CountsADocumentTypeOncePerLine()
    {
        PnlLineRow line = Line(
            revenue: 100m,
            completeness: PnlLineCompleteness.Provisional,
            types: new[] { "Commission", "Commission" });

        PnlProductDocumentGaps gaps = await AggregateAsync(line);

        gaps.Pending.MissingDocuments.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new PnlMissingDocumentCount
            {
                Type = "Commission",
                Lines = 1,
            });
    }

    [Fact]
    public async Task Aggregate_OrdersDocumentsByLineCountThenType()
    {
        PnlLineRow first = Line(
            revenue: 100m,
            completeness: PnlLineCompleteness.Provisional,
            types: new[] { "Acquiring", "Commission" });
        PnlLineRow second = Line(
            revenue: 100m,
            completeness: PnlLineCompleteness.Provisional,
            types: new[] { "Commission" });

        PnlProductDocumentGaps gaps = await AggregateAsync(first, second);

        gaps.Pending.MissingDocuments.Select(count => count.Type)
            .Should().ContainInOrder("Commission", "Acquiring");
    }

    [Fact]
    public async Task Aggregate_ReportsTheEarliestDeadlineOfTheGroup()
    {
        PnlLineRow later = Line(
            revenue: 100m,
            completeness: PnlLineCompleteness.Provisional,
            expectedBy: new DateTimeOffset(2026, 1, 14, 0, 0, 0, TimeSpan.Zero));
        PnlLineRow earlier = Line(
            revenue: 100m,
            completeness: PnlLineCompleteness.Provisional,
            expectedBy: new DateTimeOffset(2026, 1, 9, 0, 0, 0, TimeSpan.Zero));

        PnlProductDocumentGaps gaps = await AggregateAsync(later, earlier);

        gaps.Pending.EarliestExpectedBy.Should().Be(earlier.ExpectedBy);
    }

    [Fact]
    public async Task Aggregate_SumsTheControlDifferenceAndCountsOnlyNonZeroLines()
    {
        PnlLineRow withDifference = Line(revenue: 100m, completeness: PnlLineCompleteness.Complete);
        withDifference.ControlDifferenceAmount = -640m;

        PnlLineRow zeroDifference = Line(revenue: 100m, completeness: PnlLineCompleteness.Complete);
        zeroDifference.ControlDifferenceAmount = 0m;

        PnlLineRow unknownDifference = Line(revenue: 100m, completeness: PnlLineCompleteness.Complete);
        unknownDifference.ControlDifferenceAmount = null;

        PnlProductDocumentGaps gaps = await AggregateAsync(withDifference, zeroDifference, unknownDifference);

        gaps.ControlDifferenceAmount.Should().Be(-640m);
        gaps.LinesWithControlDifference.Should().Be(1);
    }

    [Fact]
    public async Task Aggregate_WithoutControlDifference_KeepsTheFigureUnknown()
    {
        PnlLineRow line = Line(revenue: 100m, completeness: PnlLineCompleteness.Complete);
        line.ControlDifferenceAmount = null;

        PnlProductDocumentGaps gaps = await AggregateAsync(line);

        gaps.ControlDifferenceAmount.Should().BeNull();
        gaps.LinesWithControlDifference.Should().Be(0);
    }

    [Fact]
    public async Task Aggregate_LineWithoutAPeriod_IsSkipped()
    {
        PnlLineRow withoutPeriod = Line(
            revenue: 100m,
            completeness: PnlLineCompleteness.Provisional,
            types: new[] { "Commission" });
        withoutPeriod.PeriodDate = null;
        withoutPeriod.SaleDate = null;

        PnlProductDocumentGaps gaps = await AggregateAsync(withoutPeriod);

        gaps.Pending.Lines.Should().Be(0);
        gaps.Pending.RevenueAmount.Should().Be(0m);
        gaps.Pending.MissingDocuments.Should().BeEmpty();
    }

    private static async Task<PnlProductDocumentGaps> AggregateAsync(params PnlLineRow[] rows)
    {
        Mock<ITenantSettingsProvider> provider = new();
        provider
            .Setup(p => p.GetTaxProfileHistoryAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantTaxProfileSnapshot>());

        PnlLineFilter filter = new();

        return await PnlDocumentGapAggregator.AggregateAsync(
            new PnlReadStoreFake(rows),
            provider.Object,
            TenantId,
            "RUB",
            filter,
            CancellationToken.None);
    }

    private static PnlLineRow Line(
        decimal revenue,
        PnlLineCompleteness completeness,
        IReadOnlyList<string>? types = null,
        DateTimeOffset? expectedBy = null)
    {
        return new PnlLineRow
        {
            Id = Guid.NewGuid(),
            OrderLineId = Guid.NewGuid(),
            ExternalOrderId = "order-1",
            ExternalProductId = "sku-1",
            OrderDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
            ChannelId = Guid.NewGuid(),
            Currency = "RUB",
            SaleDate = new DateOnly(2026, 1, 2),
            PeriodDate = new DateOnly(2026, 1, 2),
            State = PnlLineState.Calculated,
            Quantity = 1,
            RevenueAmount = revenue,
            NetRevenueAmount = revenue,
            CompletenessState = completeness,
            MissingDocumentTypes = types?.ToList() ?? new List<string>(),
            ExpectedBy = expectedBy,
            ControlDifferenceAmount = 0m,
        };
    }
}
