using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.Queries;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using Delobytes.App.Backend.Finance.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Finance;

/// <summary>
/// Tests for the P&amp;L read handlers: the profile is resolved once per distinct date, a line
/// without a sale has no tax or margin, and the summary groups by product and period.
/// </summary>
public class PnlQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task GetLines_TwoLinesOfOneDate_ResolvesTheProfileOnce()
    {
        // Arrange
        PnlLinePage page = new PnlLinePage
        {
            Rows = new List<PnlLineRow>
            {
                Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m),
                Row(new DateOnly(2026, 8, 1), 200m, 180m, 20m),
            },
            ExcludedByCurrency = 3,
        };

        Mock<ITenantSettingsProvider> provider = BuildProvider();
        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new FakeStore(page),
            BuildTenantContext(),
            provider.Object,
            BuildTimeService());

        // Act
        PnlLinesResponse response = await handler.Handle(
            new GetPnlLinesQuery { Filter = new PnlQueryFilter(), Take = 100 },
            CancellationToken.None);

        // Assert
        response.Lines.Should().HaveCount(2);
        response.Currency.Should().Be("RUB");
        response.ExcludedByCurrency.Should().Be(3);

        provider.Verify(
            p => p.GetTaxProfileAtAsync(TenantId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once);

        response.Lines[0].EstimatedTax.Should().BeApproximately(6m, 0.0001m);
        response.Lines[0].Margin.Should().BeApproximately(80m, 0.0001m);
        response.Lines[0].TaxAdjustedMargin.Should().BeApproximately(74m, 0.0001m);
        response.Lines[0].Roi.Should().BeApproximately(8m, 0.0001m);
        response.Lines[0].ComponentsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task GetLines_WithoutTaxProfile_StillReportsThePreTaxMargin()
    {
        PnlLineRow row = Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m);

        Mock<ITenantSettingsProvider> provider = BuildProviderWithoutTaxProfile();

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new FakeStore(new PnlLinePage { Rows = new List<PnlLineRow> { row } }),
            BuildTenantContext(),
            provider.Object,
            BuildTimeService());

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].EstimatedTax.Should().BeNull();
        response.Lines[0].TaxAdjustedMargin.Should().BeNull();
        response.Lines[0].Margin.Should().BeApproximately(80m, 0.0001m);
        response.Lines[0].MarginPercent.Should().BeApproximately(0.8m, 0.0001m);
        response.Lines[0].Roi.Should().BeApproximately(8m, 0.0001m);
    }

    [Fact]
    public async Task GetLines_IncompleteProductionCost_HasNoMarginEvenWithATaxProfile()
    {
        PnlLineRow row = Row(new DateOnly(2026, 8, 1), 100m, 90m, 0m, costComplete: false);

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new FakeStore(new PnlLinePage { Rows = new List<PnlLineRow> { row } }),
            BuildTenantContext(),
            BuildProvider().Object,
            BuildTimeService());

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].Margin.Should().BeNull();
        response.Lines[0].TaxAdjustedMargin.Should().BeNull();
        response.Lines[0].MarginPercent.Should().BeNull();
        response.Lines[0].Roi.Should().BeNull();
        response.Lines[0].CostComplete.Should().BeFalse();
        response.Lines[0].EstimatedTax.Should().BeApproximately(6m, 0.0001m);
    }

    [Fact]
    public async Task GetLines_LineWithoutSale_HasNoTaxOrMargin()
    {
        PnlLineRow row = Row(saleDate: null, revenue: null, netRevenue: null, cost: null);

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new FakeStore(new PnlLinePage { Rows = new List<PnlLineRow> { row } }),
            BuildTenantContext(),
            BuildProvider().Object,
            BuildTimeService());

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].EstimatedTax.Should().BeNull();
        response.Lines[0].Margin.Should().BeNull();
        response.Lines[0].TaxAdjustedMargin.Should().BeNull();
        response.Lines[0].MarginPercent.Should().BeNull();
        response.Lines[0].Roi.Should().BeNull();
    }

    [Fact]
    public async Task GetLines_SetsHasProductLinkFromRowProductId()
    {
        PnlLineRow linked = Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, Guid.NewGuid());
        PnlLineRow unlinked = Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, productId: null);

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new FakeStore(new PnlLinePage { Rows = new List<PnlLineRow> { linked, unlinked } }),
            BuildTenantContext(),
            BuildProvider().Object,
            BuildTimeService());

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].HasProductLink.Should().BeTrue();
        response.Lines[1].HasProductLink.Should().BeFalse();
    }

    [Fact]
    public async Task GetLines_ChannelProductWithoutResolvedProduct_StaysUnlinked()
    {
        Guid channelProductId = Guid.NewGuid();

        PnlLineRow row = Row(saleDate: new DateOnly(2026, 8, 1), revenue: 100m, netRevenue: 90m, cost: null);
        row.ChannelProductId = channelProductId;

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new FakeStore(new PnlLinePage { Rows = new List<PnlLineRow> { row } }),
            BuildTenantContext(),
            BuildProvider().Object,
            BuildTimeService());

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].ChannelProductId.Should().Be(channelProductId);
        response.Lines[0].ProductId.Should().BeNull();
        response.Lines[0].HasProductLink.Should().BeFalse();
    }

    [Fact]
    public async Task GetSummary_GroupsByProductAndMonth()
    {
        Guid productId = Guid.NewGuid();

        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, productId),
            Row(new DateOnly(2026, 8, 15), 100m, 90m, 10m, productId),
            Row(new DateOnly(2026, 9, 1), 200m, 180m, 20m, productId),
        };

        GetPnlSummaryQueryHandler handler = new GetPnlSummaryQueryHandler(
            new FakeStore(new PnlLinePage(), rows),
            BuildTenantContext(),
            BuildProvider().Object,
            BuildTimeService());

        PnlSummaryResponse response = await handler.Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month },
            CancellationToken.None);

        response.Rows.Should().HaveCount(2);
        response.Rows[0].PeriodStart.Should().Be(new DateOnly(2026, 8, 1));
        response.Rows[0].Lines.Should().Be(2);
        response.Rows[0].RevenueAmount.Should().Be(200m);
        response.Rows[1].PeriodStart.Should().Be(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public async Task GetSummary_MarksRowsWithoutProductLinkAsUnlinked()
    {
        Guid productId = Guid.NewGuid();

        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, productId),
            Row(new DateOnly(2026, 8, 1), 50m, 45m, 5m, productId: null),
        };

        GetPnlSummaryQueryHandler handler = new GetPnlSummaryQueryHandler(
            new FakeStore(new PnlLinePage(), rows),
            BuildTenantContext(),
            BuildProvider().Object,
            BuildTimeService());

        PnlSummaryResponse response = await handler.Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Day },
            CancellationToken.None);

        response.Rows.Should().HaveCount(2);
        response.Rows.Single(r => r.ProductId == productId).IsUnlinkedProduct.Should().BeFalse();
        response.Rows.Single(r => r.ProductId == null).IsUnlinkedProduct.Should().BeTrue();
    }

    [Fact]
    public async Task GetSummary_MixedCostCompleteness_ReportsTheMarginOfTheCoveredLinesOnly()
    {
        // One line with a complete cost, one with a partial cost and one without any cost. The
        // margin can speak only for the first line, so its percentages must use that line alone
        // while the coverage fields keep the rest of the revenue visible.
        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 8, 1), 1000m, 700m, 300m),
            Row(new DateOnly(2026, 8, 1), 1000m, 700m, 250m, costComplete: false),
            Row(new DateOnly(2026, 8, 1), 1000m, 700m, cost: null),
        };

        PnlSummaryResponse response = await BuildSummaryHandler(rows).Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.RevenueAmount.Should().Be(3000m);
        row.ProductionCostAmount.Should().Be(550m);
        row.Margin.Should().Be(400m);

        row.Lines.Should().Be(3);
        row.LinesWithMargin.Should().Be(1);
        row.RevenueWithMarginAmount.Should().Be(1000m);
        row.ProductionCostWithMarginAmount.Should().Be(300m);
        row.RevenueWithoutCostAmount.Should().Be(2000m);
        row.IsMarginComplete.Should().BeFalse();

        row.RevenueWithMarginAmount.Should().Be(row.RevenueAmount!.Value - row.RevenueWithoutCostAmount);

        row.MarginPercent.Should().BeApproximately(0.4m, 0.0001m);
        row.Roi.Should().BeApproximately(4m / 3m, 0.0001m);
    }

    [Fact]
    public async Task GetSummary_TaxesStayAttributedToTheLinesThatCarryThem()
    {
        // The tax layer is an intersection of two populations: a known margin and a tax profile.
        // The pre-tax margin must not absorb a tax computed on revenue it does not cover.
        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 8, 1), 1000m, 700m, 300m),
            Row(new DateOnly(2026, 8, 1), 1000m, 700m, 250m, costComplete: false),
        };

        PnlSummaryResponse response = await BuildSummaryHandler(rows).Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.EstimatedTax.Should().Be(120m);
        row.Margin.Should().Be(400m);
        row.TaxAdjustedMargin.Should().Be(340m);
    }

    [Fact]
    public async Task GetSummary_AllCostsComplete_IsMarginCompleteAndReconciles()
    {
        Guid productId = Guid.NewGuid();

        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, productId),
            Row(new DateOnly(2026, 8, 2), 100m, 90m, 10m, productId),
        };

        PnlSummaryResponse response = await BuildSummaryHandler(rows).Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.IsMarginComplete.Should().BeTrue();
        row.LinesWithMargin.Should().Be(2);
        row.RevenueWithMarginAmount.Should().Be(200m);
        row.ProductionCostWithMarginAmount.Should().Be(20m);
        row.RevenueWithoutCostAmount.Should().Be(0m);
        row.Margin.Should().Be(160m);
        row.MarginPercent.Should().BeApproximately(0.8m, 0.0001m);
        row.Roi.Should().BeApproximately(8m, 0.0001m);
    }

    [Fact]
    public async Task GetSummary_NoCostAtAll_ReportsNoDerivedMarginButKeepsTheRevenue()
    {
        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 8, 1), 1000m, 700m, cost: null),
            Row(new DateOnly(2026, 8, 2), 1000m, 700m, cost: null),
            Row(new DateOnly(2026, 8, 3), 1000m, 700m, cost: null),
        };

        PnlSummaryResponse response = await BuildSummaryHandler(rows).Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.Margin.Should().BeNull();
        row.MarginPercent.Should().BeNull();
        row.Roi.Should().BeNull();
        row.LinesWithMargin.Should().Be(0);
        row.RevenueWithMarginAmount.Should().Be(0m);
        row.ProductionCostWithMarginAmount.Should().Be(0m);
        row.RevenueWithoutCostAmount.Should().Be(3000m);
        row.IsMarginComplete.Should().BeFalse();
    }

    [Fact]
    public async Task GetSummary_CompletenessState_ReportsTheMostSevereLine()
    {
        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, completeness: PnlLineCompleteness.ClosedWithException),
            Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, completeness: PnlLineCompleteness.Overdue),
            Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, completeness: PnlLineCompleteness.Provisional),
        };

        PnlSummaryResponse response = await BuildSummaryHandler(rows).Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month },
            CancellationToken.None);

        response.Rows.Single().CompletenessState.Should().Be(PnlLineCompleteness.Overdue);
    }

    private static GetPnlSummaryQueryHandler BuildSummaryHandler(IReadOnlyList<PnlLineRow> rows)
    {
        return new GetPnlSummaryQueryHandler(
            new FakeStore(new PnlLinePage(), rows),
            BuildTenantContext(),
            BuildProvider().Object,
            BuildTimeService());
    }

    private static PnlLineRow Row(
        DateOnly? saleDate,
        decimal? revenue,
        decimal? netRevenue,
        decimal? cost,
        Guid? productId = null,
        bool costComplete = true,
        PnlLineCompleteness completeness = PnlLineCompleteness.Complete)
    {
        return new PnlLineRow
        {
            OrderLineId = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            ProductId = productId,
            Currency = "RUB",
            SaleDate = saleDate,
            State = saleDate.HasValue ? PnlLineState.Calculated : PnlLineState.NotCalculated,
            RevenueAmount = revenue,
            NetRevenueAmount = netRevenue,
            ProductionCostAmount = cost,
            ControlDifferenceAmount = 0m,
            ProductionCostIsComplete = cost.HasValue ? costComplete : null,
            CostStatus = OrderLineCostStatus.Resolved,
            CompletenessState = completeness,
        };
    }

    private static ITenantContext BuildTenantContext()
    {
        Mock<ITenantContext> tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns(TenantId);
        return tenant.Object;
    }

    private static Mock<ITenantSettingsProvider> BuildProvider()
    {
        Mock<ITenantSettingsProvider> provider = new Mock<ITenantSettingsProvider>();

        provider
            .Setup(p => p.GetAccountingSettingsAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantAccountingSettings { Currency = "RUB", TimeZoneId = "UTC" });

        provider
            .Setup(p => p.GetTaxProfileAtAsync(TenantId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantTaxProfileSnapshot
            {
                Regime = TaxRegime.UsnIncome,
                RatePercent = 6m,
                Vat = VatType.None,
            });

        return provider;
    }

    private static Mock<ITenantSettingsProvider> BuildProviderWithoutTaxProfile()
    {
        Mock<ITenantSettingsProvider> provider = new Mock<ITenantSettingsProvider>();

        provider
            .Setup(p => p.GetAccountingSettingsAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantAccountingSettings { Currency = "RUB", TimeZoneId = "UTC" });

        provider
            .Setup(p => p.GetTaxProfileAtAsync(TenantId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantTaxProfileSnapshot?)null);

        return provider;
    }

    private static ITenantTimeService BuildTimeService()
    {
        Mock<ITenantTimeService> time = new Mock<ITenantTimeService>();
        time.Setup(t => t.GetTimeZoneAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TimeZoneInfo.Utc);
        return time.Object;
    }

    private sealed class FakeStore : IPnlReadStore
    {
        private readonly PnlLinePage _page;
        private readonly IReadOnlyList<PnlLineRow> _rows;

        public FakeStore(PnlLinePage page, IReadOnlyList<PnlLineRow>? rows = null)
        {
            _page = page;
            _rows = rows ?? page.Rows;
        }

        public Task<PnlLinePage> GetLinesAsync(
            string currency,
            PnlLineFilter filter,
            int take,
            string? afterToken,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_page);
        }

        public Task<IReadOnlyList<PnlLineRow>> GetForSummaryAsync(
            string currency,
            PnlLineFilter filter,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_rows);
        }

        public Task<PnlDiagnosticsResult> GetDiagnosticsAsync(
            DateOnly from,
            DateOnly to,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new PnlDiagnosticsResult());
        }
    }
}


