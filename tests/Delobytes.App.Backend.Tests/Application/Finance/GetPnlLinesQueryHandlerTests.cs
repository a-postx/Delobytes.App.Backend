using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Contracts.Lookup;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.ProductTable;
using Delobytes.App.Backend.Finance.Application.Queries;
using Delobytes.App.Backend.Finance.Application.Ranking;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using Delobytes.App.Backend.Finance.Domain.Enums;
using Delobytes.App.Backend.Tests.Application.Finance.Fakes;
using FluentAssertions;
using FluentValidation;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Finance;

/// <summary>
/// P&amp;L line page: the tax profile is resolved once per report, a line without a sale has no tax or margin, and the marketplace identifiers reach the response.
/// </summary>
public class GetPnlLinesQueryHandlerTests
{
    [Fact]
    public async Task GetLines_ManyLinesOfOneDate_LoadsTheTaxProfileHistoryOnce()
    {
        // Arrange
        PnlLinePage page = new PnlLinePage
        {
            Rows = new List<PnlLineRow>
            {
                PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m),
                PnlTestData.Row(new DateOnly(2026, 8, 1), 200m, 180m, 20m),
            },
            ExcludedByCurrency = 3,
        };

        Mock<ITenantSettingsProvider> provider = PnlTestData.SettingsProvider();
        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new PnlReadStoreFake(page),
            PnlTestData.TenantContext(),
            provider.Object, PnlTestData.Lookup().Object);

        // Act
        PnlLinesResponse response = await handler.Handle(
            new GetPnlLinesQuery { Filter = new PnlQueryFilter(), Take = 100 },
            CancellationToken.None);

        // Assert
        response.Lines.Should().HaveCount(2);
        response.Currency.Should().Be("RUB");
        response.ExcludedByCurrency.Should().Be(3);

        // The history is the whole tax timeline, not one lookup per distinct sale date: the number
        // of round trips must not grow with the length of the report.
        provider.Verify(
            p => p.GetTaxProfileHistoryAsync(PnlTestData.TenantId, It.IsAny<CancellationToken>()),
            Times.Once);

        provider.Verify(
            p => p.GetTaxProfileAtAsync(PnlTestData.TenantId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);

        response.Lines[0].EstimatedTax.Should().BeApproximately(6m, 0.0001m);
        response.Lines[0].Margin.Should().BeApproximately(80m, 0.0001m);
        response.Lines[0].TaxAdjustedMargin.Should().BeApproximately(74m, 0.0001m);
        response.Lines[0].RoiPercent.Should().BeApproximately(800m, 0.0001m);
        response.Lines[0].ComponentsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task GetLines_WithoutTaxProfile_StillReportsThePreTaxMargin()
    {
        PnlLineRow row = PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m);

        Mock<ITenantSettingsProvider> provider = PnlTestData.SettingsProviderWithoutTaxProfile();

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new PnlReadStoreFake(new PnlLinePage { Rows = new List<PnlLineRow> { row } }),
            PnlTestData.TenantContext(),
            provider.Object, PnlTestData.Lookup().Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].EstimatedTax.Should().BeNull();
        response.Lines[0].TaxAdjustedMargin.Should().BeNull();
        response.Lines[0].Margin.Should().BeApproximately(80m, 0.0001m);
        response.Lines[0].ContributionMarginPercent.Should().BeApproximately(80m / 90m * 100m, 0.0001m);
        response.Lines[0].ReturnOnSoldCostPercent.Should().BeApproximately(800m, 0.0001m);
        response.Lines[0].RoiPercent.Should().BeApproximately(800m, 0.0001m);
    }

    [Fact]
    public async Task GetLines_IncompleteProductionCost_HasNoMarginEvenWithATaxProfile()
    {
        PnlLineRow row = PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 0m, costComplete: false);

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new PnlReadStoreFake(new PnlLinePage { Rows = new List<PnlLineRow> { row } }),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object, PnlTestData.Lookup().Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].Margin.Should().BeNull();
        response.Lines[0].TaxAdjustedMargin.Should().BeNull();
        response.Lines[0].ContributionMarginPercent.Should().BeNull();
        response.Lines[0].ReturnOnSoldCostPercent.Should().BeNull();
        response.Lines[0].RoiPercent.Should().BeNull();
        response.Lines[0].CostComplete.Should().BeFalse();
        response.Lines[0].EstimatedTax.Should().BeApproximately(6m, 0.0001m);
    }

    [Fact]
    public async Task GetLines_LineWithoutSale_ReportsAZeroTaxAndNoMargin()
    {
        PnlLineRow row = PnlTestData.Row(saleDate: null, revenue: null, netRevenue: null, cost: null);

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new PnlReadStoreFake(new PnlLinePage { Rows = new List<PnlLineRow> { row } }),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object, PnlTestData.Lookup().Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        // No sale means a zero tax base, so the tax is a known zero even without a profile; the margin
        // stays unknown because the line reported no money at all.
        response.Lines[0].EstimatedTax.Should().Be(0m);
        response.Lines[0].Margin.Should().BeNull();
        response.Lines[0].TaxAdjustedMargin.Should().BeNull();
        response.Lines[0].ContributionMarginPercent.Should().BeNull();
        response.Lines[0].ReturnOnSoldCostPercent.Should().BeNull();
        response.Lines[0].RoiPercent.Should().BeNull();
        response.Lines[0].TaxAdjustedContributionMarginPercent.Should().BeNull();
        response.Lines[0].TaxAdjustedRoiPercent.Should().BeNull();
    }

    [Fact]
    public async Task GetLines_SetsHasProductLinkFromRowProductId()
    {
        PnlLineRow linked = PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, Guid.NewGuid());
        PnlLineRow unlinked = PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, productId: null);

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new PnlReadStoreFake(new PnlLinePage { Rows = new List<PnlLineRow> { linked, unlinked } }),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object, PnlTestData.Lookup().Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].HasProductLink.Should().BeTrue();
        response.Lines[1].HasProductLink.Should().BeFalse();
    }

    [Theory]
    [InlineData("order-9", "line-7", "sku-42")]
    [InlineData("order-1", null, "barcode-11")]
    public async Task GetLines_ExposesTheMarketplaceIdentifiersFromTheReference(
        string externalOrderId,
        string? externalLineId,
        string externalProductId)
    {
        PnlLineRow row = PnlTestData.Row(
            new DateOnly(2026, 8, 1),
            100m,
            90m,
            10m,
            externalOrderId: externalOrderId,
            externalLineId: externalLineId,
            externalProductId: externalProductId);

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new PnlReadStoreFake(new PnlLinePage { Rows = new List<PnlLineRow> { row } }),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object, PnlTestData.Lookup().Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].ExternalOrderId.Should().Be(externalOrderId);
        response.Lines[0].ExternalLineId.Should().Be(externalLineId);
        response.Lines[0].ExternalProductId.Should().Be(externalProductId);
    }

    [Fact]
    public async Task GetLines_OrderDateAtNoonUtc_StaysOnTheSameCalendarDate()
    {
        // The same instant, read in a UTC tenant's calendar, keeps its date; the conversion must not
        // shift a noontime UTC instant across the date line.
        PnlLineRow row = PnlTestData.Row(
            new DateOnly(2026, 8, 1),
            100m,
            90m,
            10m,
            orderDate: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero));

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new PnlReadStoreFake(new PnlLinePage { Rows = new List<PnlLineRow> { row } }),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider("UTC").Object, PnlTestData.Lookup().Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].OrderDate.Should().Be(new DateOnly(2026, 8, 1));
    }

    [Fact]
    public async Task GetLines_OrderDateInVladivostok_IsConvertedFromUtcToTheTenantCalendar()
    {
        // 2026-08-31 21:00 UTC is already 2026-09-01 in Asia/Vladivostok (UTC+10). The tenant-facing
        // date must be the local reading of the instant, not the UTC one.
        PnlLineRow row = PnlTestData.Row(
            new DateOnly(2026, 8, 31),
            100m,
            90m,
            10m,
            orderDate: new DateTimeOffset(2026, 8, 31, 21, 0, 0, TimeSpan.Zero));

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new PnlReadStoreFake(new PnlLinePage { Rows = new List<PnlLineRow> { row } }),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider("Asia/Vladivostok").Object, PnlTestData.Lookup().Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].OrderDate.Should().Be(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public async Task GetLines_OrderDateWestOfTheMeridian_IsReadBackIntoThePreviousDay()
    {
        // 2026-08-01 01:00 UTC is still 2026-07-31 in America/Los_Angeles (UTC-7 in August). A zone
        // behind UTC must shift the date backwards, not forwards.
        PnlLineRow row = PnlTestData.Row(
            new DateOnly(2026, 8, 1),
            100m,
            90m,
            10m,
            orderDate: new DateTimeOffset(2026, 8, 1, 1, 0, 0, TimeSpan.Zero));

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new PnlReadStoreFake(new PnlLinePage { Rows = new List<PnlLineRow> { row } }),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider("America/Los_Angeles").Object, PnlTestData.Lookup().Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].OrderDate.Should().Be(new DateOnly(2026, 7, 31));
    }

    [Fact]
    public async Task GetLines_ChannelProductWithoutResolvedProduct_StaysUnlinked()
    {
        Guid channelProductId = Guid.NewGuid();

        PnlLineRow row = PnlTestData.Row(saleDate: new DateOnly(2026, 8, 1), revenue: 100m, netRevenue: 90m, cost: null);
        row.ChannelProductId = channelProductId;

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new PnlReadStoreFake(new PnlLinePage { Rows = new List<PnlLineRow> { row } }),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object, PnlTestData.Lookup().Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].ChannelProductId.Should().Be(channelProductId);
        response.Lines[0].ProductId.Should().BeNull();
        response.Lines[0].HasProductLink.Should().BeFalse();
    }

    [Fact]
    public async Task GetLines_DefaultCompleteness_SelectsTheFinalStates()
    {
        PnlReadStoreFake store = new PnlReadStoreFake(new PnlLinePage { Rows = new List<PnlLineRow>() });

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            store,
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object, PnlTestData.Lookup().Object);

        await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        store.LastFilter.Should().NotBeNull();
        store.LastFilter!.CompletenessStates.Should().BeEquivalentTo(
            new[] { PnlLineCompleteness.Complete, PnlLineCompleteness.ClosedWithException });
    }

    [Fact]
    public async Task GetLines_LineWithoutSale_ReportsItsNetRevenueAsTheResult()
    {
        // A non-buyout carries only the delivery charge: no unit left the seller, so no cost of goods
        // applies and the negative net revenue is the known result of the line.
        PnlLineRow row = PnlTestData.Row(
            saleDate: null,
            revenue: null,
            netRevenue: -53.84m,
            cost: null,
            periodDate: new DateOnly(2026, 8, 5));

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new PnlReadStoreFake(new PnlLinePage { Rows = new List<PnlLineRow> { row } }),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object, PnlTestData.Lookup().Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].Margin.Should().Be(-53.84m);
        response.Lines[0].PeriodDate.Should().Be(new DateOnly(2026, 8, 5));
        response.Lines[0].ContributionMarginPercent.Should().BeNull();
        response.Lines[0].RoiPercent.Should().BeNull();
    }

    [Fact]
    public async Task GetLines_LineWithoutSale_HasAZeroTaxInsteadOfAnUnknownOne()
    {
        // No sale means no sale revenue, so the tax base is a known zero and the tax is zero for every
        // rate. Reporting null here would leave every group holding a non-buyout tax-incomplete.
        PnlLineRow row = PnlTestData.Row(
            saleDate: null,
            revenue: null,
            netRevenue: -53.84m,
            cost: null,
            periodDate: new DateOnly(2026, 8, 5));

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new PnlReadStoreFake(new PnlLinePage { Rows = new List<PnlLineRow> { row } }),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProviderWithoutTaxProfile().Object, PnlTestData.Lookup().Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].EstimatedTax.Should().Be(0m);
        response.Lines[0].Margin.Should().Be(-53.84m);
        response.Lines[0].TaxAdjustedMargin.Should().Be(-53.84m);

        // The tax-adjusted figures still need a positive base, which a delivery charge never gives.
        response.Lines[0].TaxAdjustedContributionMarginPercent.Should().BeNull();
        response.Lines[0].TaxAdjustedRoiPercent.Should().BeNull();
    }

    [Fact]
    public async Task GetLines_TaxAdjustedPercentages_CoverTheSameLineAsTheirBase()
    {
        PnlLineRow row = PnlTestData.Row(new DateOnly(2026, 8, 1), revenue: 1000m, netRevenue: 700m, cost: 300m);

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new PnlReadStoreFake(new PnlLinePage { Rows = new List<PnlLineRow> { row } }),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object, PnlTestData.Lookup().Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].EstimatedTax.Should().BeApproximately(60m, 0.0001m);
        response.Lines[0].TaxAdjustedMargin.Should().BeApproximately(340m, 0.0001m);
        response.Lines[0].ContributionMarginPercent.Should().BeApproximately(400m / 700m * 100m, 0.0001m);
        response.Lines[0].TaxAdjustedContributionMarginPercent
            .Should().BeApproximately(340m / 700m * 100m, 0.0001m);
        response.Lines[0].RoiPercent.Should().BeApproximately(400m / 300m * 100m, 0.0001m);
        response.Lines[0].TaxAdjustedRoiPercent.Should().BeApproximately(340m / 300m * 100m, 0.0001m);
    }

    [Fact]
    public async Task GetLines_LineTotalCost_IsProratedToTheOrderedQuantity()
    {
        // The cost fact was resolved for four units, the order carries three, and the fact has no
        // unit price. The investment must be 200 * 3 / 4, not the whole line total.
        PnlLineRow row = PnlTestData.Row(
            saleDate: new DateOnly(2026, 8, 1),
            revenue: 300m,
            netRevenue: 210m,
            cost: 150m,
            quantity: 3,
            costTotal: 200m,
            costQuantity: 4);

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new PnlReadStoreFake(new PnlLinePage { Rows = new List<PnlLineRow> { row } }),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object, PnlTestData.Lookup().Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].InvestedCostAmount.Should().Be(150m);
        response.Lines[0].Margin.Should().Be(60m);
        response.Lines[0].RoiPercent.Should().BeApproximately(40m, 0.0001m);
        response.Lines[0].ReturnOnSoldCostPercent.Should().BeApproximately(40m, 0.0001m);
    }

    [Fact]
    public async Task GetLines_LineWithoutSale_WithAKnownCost_CarriesAnInvestmentAndNoReturn()
    {
        // The unit came back, so no cost of goods is charged, but the money tied up in it is still
        // known: the line shows an investment with a zero return rather than no investment at all.
        PnlLineRow row = PnlTestData.Row(
            saleDate: null,
            revenue: null,
            netRevenue: -100m,
            cost: null,
            periodDate: new DateOnly(2026, 8, 5),
            unitCost: 300m);

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new PnlReadStoreFake(new PnlLinePage { Rows = new List<PnlLineRow> { row } }),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object, PnlTestData.Lookup().Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].InvestedCostAmount.Should().Be(300m);
        response.Lines[0].Margin.Should().Be(-100m);
        response.Lines[0].RoiPercent.Should().BeApproximately(-100m / 300m * 100m, 0.0001m);
        response.Lines[0].ReturnOnSoldCostPercent.Should().BeNull();
    }

    [Fact]
    public async Task GetLines_ClosedWithExceptionLine_IsNotReportedAsProvisional()
    {
        PnlLineRow row = PnlTestData.Row(
            new DateOnly(2026, 8, 1), 100m, 90m, 10m, completeness: PnlLineCompleteness.ClosedWithException);

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new PnlReadStoreFake(new PnlLinePage { Rows = new List<PnlLineRow> { row } }),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object, PnlTestData.Lookup().Object);

        PnlLinesResponse response = await handler.Handle(
            new GetPnlLinesQuery { Filter = new PnlQueryFilter { Completeness = "all" } },
            CancellationToken.None);

        response.Lines[0].CompletenessState.Should().Be(PnlLineCompleteness.ClosedWithException);
        response.Lines[0].IsProvisional.Should().BeFalse();
    }

    [Fact]
    public async Task GetLines_TwoDatesAcrossAProfileChange_ApplyTheProfileOfEachDate()
    {
        // A single history load must still resolve the right profile per date: the report covers
        // both sides of the change, so a stale flat rate would misstate one of them.
        PnlLineRow beforeChange = PnlTestData.Row(new DateOnly(2025, 12, 31), revenue: 100m, netRevenue: 90m, cost: 10m);
        PnlLineRow afterChange = PnlTestData.Row(new DateOnly(2026, 1, 1), revenue: 100m, netRevenue: 90m, cost: 10m);

        Mock<ITenantSettingsProvider> provider = new Mock<ITenantSettingsProvider>();

        provider
            .Setup(p => p.GetAccountingSettingsAsync(PnlTestData.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantAccountingSettings { Currency = "RUB", TimeZoneId = "UTC" });

        provider
            .Setup(p => p.GetTaxProfileHistoryAsync(PnlTestData.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantTaxProfileSnapshot>
            {
                new()
                {
                    Regime = TaxRegime.UsnIncome,
                    RatePercent = 15m,
                    Vat = VatType.None,
                    ValidFrom = new DateOnly(2026, 1, 1),
                },
                new()
                {
                    Regime = TaxRegime.UsnIncome,
                    RatePercent = 6m,
                    Vat = VatType.None,
                    ValidFrom = new DateOnly(2025, 1, 1),
                },
            });

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new PnlReadStoreFake(new PnlLinePage { Rows = new List<PnlLineRow> { beforeChange, afterChange } }),
            PnlTestData.TenantContext(),
            provider.Object, PnlTestData.Lookup().Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].EstimatedTax.Should().BeApproximately(6m, 0.0001m);
        response.Lines[1].EstimatedTax.Should().BeApproximately(15m, 0.0001m);
    }

    [Fact]
    public async Task GetLines_DateBeforeEveryProfile_ReportsNoTax()
    {
        PnlLineRow row = PnlTestData.Row(new DateOnly(2024, 12, 31), revenue: 100m, netRevenue: 90m, cost: 10m);

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new PnlReadStoreFake(new PnlLinePage { Rows = new List<PnlLineRow> { row } }),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object, PnlTestData.Lookup().Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].EstimatedTax.Should().BeNull();
        response.Lines[0].TaxAdjustedMargin.Should().BeNull();
        response.Lines[0].Margin.Should().BeApproximately(80m, 0.0001m);
    }

    [Fact]
    public async Task GetLines_ReportsTheOrderedAndTheSoldQuantityApart()
    {
        // A non-buyout was ordered but never sold: it must add to the ordered units only, otherwise
        // a per-unit margin would be divided by units that never left the seller.
        PnlLineRow sold = PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, quantity: 3);
        PnlLineRow notBoughtOut = PnlTestData.Row(
            saleDate: null,
            revenue: null,
            netRevenue: -53.84m,
            cost: null,
            periodDate: new DateOnly(2026, 8, 5),
            quantity: 2);

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new PnlReadStoreFake(new PnlLinePage { Rows = new List<PnlLineRow> { sold, notBoughtOut } }),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object, PnlTestData.Lookup().Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].QuantityOrdered.Should().Be(3);
        response.Lines[0].QuantitySold.Should().Be(3);
        response.Lines[1].QuantityOrdered.Should().Be(2);
        response.Lines[1].QuantitySold.Should().Be(0);
    }

    [Fact]
    public async Task GetLines_ReturnedUnit_IsOrderedButNoLongerSold()
    {
        // The unit sold and came back: the money of the return is what is left, and no cost of goods
        // was consumed, so the line must not be divided by a cost it does not carry.
        PnlLineRow returned = PnlTestData.Row(
            new DateOnly(2026, 8, 1),
            revenue: 0m,
            netRevenue: 0m,
            cost: null,
            quantity: 1,
            returnedQuantity: 1);

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new PnlReadStoreFake(new PnlLinePage { Rows = new List<PnlLineRow> { returned } }),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object, PnlTestData.Lookup().Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].QuantityOrdered.Should().Be(1);
        response.Lines[0].QuantitySold.Should().Be(0);
        response.Lines[0].Margin.Should().Be(0m);
    }

    [Fact]
    public async Task GetLines_EnrichesLinkedLinesOnly()
    {
        // Arrange
        Guid productId = Guid.NewGuid();
        PnlLineRow linked = PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, productId);
        PnlLineRow unlinked = PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, productId: null);

        Mock<IProductSkuLookup> lookup = PnlTestData.Lookup();
        lookup
            .Setup(l => l.GetSkusAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [productId] = "SKU-7" });

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new PnlReadStoreFake(new PnlLinePage { Rows = new List<PnlLineRow> { linked, unlinked } }),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object,
            lookup.Object);

        // Act
        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        // Assert
        response.Lines.Should().HaveCount(2);
        response.Lines.Should().ContainSingle(line => line.Sku == "SKU-7");
        lookup.Verify(
            l => l.GetSkusAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(productId)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetLines_NoProductLinks_SkipsTheLookup()
    {
        // Arrange: a page of fully unlinked lines has nothing to label.
        PnlLineRow first = PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, productId: null);
        PnlLineRow second = PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, productId: null);

        Mock<IProductSkuLookup> lookup = PnlTestData.Lookup();
        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new PnlReadStoreFake(new PnlLinePage { Rows = new List<PnlLineRow> { first, second } }),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object,
            lookup.Object);

        // Act
        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        // Assert
        response.Lines.Should().HaveCount(2).And.OnlyContain(line => line.Sku == null);
        lookup.Verify(
            l => l.GetSkusAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetLines_MultipleProductIds_ArePassedToTheStore()
    {
        // Arrange
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        PnlReadStoreFake store = new PnlReadStoreFake(new PnlLinePage { Rows = new List<PnlLineRow>() });

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            store,
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object,
            PnlTestData.Lookup().Object);

        // Act
        await handler.Handle(
            new GetPnlLinesQuery
            {
                Filter = new PnlQueryFilter { ProductIds = new[] { first, second } },
            },
            CancellationToken.None);

        // Assert
        store.LastFilter.Should().NotBeNull();
        store.LastFilter!.ProductIds.Should().BeEquivalentTo(new[] { first, second });
    }

    [Fact]
    public async Task GetLines_PassesTheSortAndTheParsedCursorToTheStore()
    {
        // Arrange
        PnlReadStoreFake store = new PnlReadStoreFake(new PnlLinePage { Rows = new List<PnlLineRow>() });

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            store,
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object,
            PnlTestData.Lookup().Object);

        PnlLineSort sort = new PnlLineSort { Key = PnlLineSortKey.Margin, Descending = false };
        Guid cursorId = Guid.NewGuid();
        string token = $"margin|asc|1234.50|{cursorId}";

        // Act
        await handler.Handle(
            new GetPnlLinesQuery { Sort = sort, ContinuationToken = token },
            CancellationToken.None);

        // Assert
        store.LastSort.Should().NotBeNull();
        store.LastSort!.Key.Should().Be(PnlLineSortKey.Margin);
        store.LastSort.Descending.Should().BeFalse();

        store.LastCursor.Should().NotBeNull();
        store.LastCursor!.Id.Should().Be(cursorId);
        store.LastCursor.MarginValue.Should().Be(1234.50m);
    }

    [Fact]
    public async Task GetLines_TokenOfAnotherSort_IsRejected()
    {
        // A token carries the ordering that produced it: continuing another order with it would
        // silently skip or repeat rows, so the request is refused instead.
        PnlReadStoreFake store = new PnlReadStoreFake(new PnlLinePage { Rows = new List<PnlLineRow>() });

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            store,
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object,
            PnlTestData.Lookup().Object);

        GetPnlLinesQuery query = new GetPnlLinesQuery
        {
            Sort = new PnlLineSort { Key = PnlLineSortKey.Date, Descending = true },
            ContinuationToken = $"margin|desc|10.00|{Guid.NewGuid()}",
        };

        Func<Task> act = () => handler.Handle(query, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        store.LastSort.Should().BeNull();
    }

    [Fact]
    public async Task GetLines_ExposesTheSignedDeductionBreakdown()
    {
        // The components keep their journal sign, so a fee is negative, and the sale plus every
        // component is the net revenue the line already carries. A line that reported nothing keeps
        // nulls: an unreported component is not a zero.
        PnlLineRow withDeductions = PnlTestData.Row(
            new DateOnly(2026, 8, 1),
            revenue: 1000m,
            netRevenue: 639m,
            cost: 300m,
            commission: -250m,
            acquiring: -20m,
            deliveryFee: -30m,
            refund: -61m,
            compensation: 0m);

        PnlLineRow withoutDeductions = PnlTestData.Row(new DateOnly(2026, 8, 2), revenue: 500m, netRevenue: 500m, cost: 200m);

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new PnlReadStoreFake(new PnlLinePage { Rows = new List<PnlLineRow> { withDeductions, withoutDeductions } }),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object, PnlTestData.Lookup().Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        PnlLineView line = response.Lines[0];

        line.CommissionAmount.Should().Be(-250m);
        line.AcquiringAmount.Should().Be(-20m);
        line.DeliveryFeeAmount.Should().Be(-30m);
        line.RefundAmount.Should().Be(-61m);
        line.CompensationAmount.Should().Be(0m);
        line.NetRevenueAmount.Should().Be(639m);

        (line.RevenueAmount!.Value
            + line.CommissionAmount!.Value
            + line.AcquiringAmount!.Value
            + line.DeliveryFeeAmount!.Value
            + line.RefundAmount!.Value
            + line.CompensationAmount!.Value)
            .Should().Be(line.NetRevenueAmount!.Value);

        PnlLineView bare = response.Lines[1];

        bare.CommissionAmount.Should().BeNull();
        bare.AcquiringAmount.Should().BeNull();
        bare.DeliveryFeeAmount.Should().BeNull();
        bare.RefundAmount.Should().BeNull();
        bare.CompensationAmount.Should().BeNull();
    }
}
