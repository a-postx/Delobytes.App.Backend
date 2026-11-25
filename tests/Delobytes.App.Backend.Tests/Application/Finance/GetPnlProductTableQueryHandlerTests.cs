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
/// P&amp;L product table: segmentation, search, sorting, paging and the currency exclusions reported next to the page.
/// </summary>
public class GetPnlProductTableQueryHandlerTests
{
    [Fact]
    public async Task GetProductTable_KeepsTheUnlinkedSaleAsideFromEveryProductFigure()
    {
        // Arrange: three products with different coverage and one sale without a catalogue link.
        Guid profitable = Guid.NewGuid();
        Guid loss = Guid.NewGuid();
        Guid withoutCost = Guid.NewGuid();

        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 8, 1), 1000m, 700m, 300m, profitable),
            PnlTestData.Row(new DateOnly(2026, 8, 1), 500m, 450m, 600m, loss),
            PnlTestData.Row(new DateOnly(2026, 8, 1), 300m, 300m, cost: null, productId: withoutCost),
            PnlTestData.Row(new DateOnly(2026, 8, 1), 200m, 200m, cost: null, productId: null),
        };

        GetPnlProductTableQueryHandler handler = new GetPnlProductTableQueryHandler(
            new PnlReadStoreFake(new PnlLinePage(), rows),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object,
            PnlTestData.Lookup().Object);

        // Act
        PnlProductTableResponse response = await handler.Handle(
            new GetPnlProductTableQuery
            {
                Filter = new PnlQueryFilter
                {
                    From = new DateOnly(2026, 8, 1),
                    To = new DateOnly(2026, 8, 31),
                },
                PageSize = 25,
            },
            CancellationToken.None);

        // Assert: the aggregate of unlinked sales is reported next to the table, not as a product.
        response.TotalCount.Should().Be(3);
        response.Items.Should().HaveCount(3).And.OnlyContain(row => row.ProductId != null);

        response.Counts.All.Should().Be(3);
        response.Counts.Loss.Should().Be(1);
        response.Counts.WithoutCost.Should().Be(1);

        response.Summary.Products.Should().Be(3);
        response.Summary.LossProducts.Should().Be(1);
        response.Summary.LossMarginAmount.Should().Be(-150m);
        response.Summary.WithoutCostProducts.Should().Be(1);
        response.Summary.WithoutCostNetRevenueAmount.Should().Be(300m);

        response.Unlinked.Should().NotBeNull();
        response.Unlinked!.Lines.Should().Be(1);
        response.Unlinked.RevenueAmount.Should().Be(200m);
        response.Unlinked.NetRevenueAmount.Should().Be(200m);

        response.Currency.Should().Be("RUB");
    }

    [Fact]
    public async Task GetProductTable_NoSearchAndMetricSort_LabelsOnlyTheServedPage()
    {
        // Arrange: three products, one row per page. A product outside the page costs no lookup.
        Guid top = Guid.NewGuid();
        Guid middle = Guid.NewGuid();
        Guid bottom = Guid.NewGuid();

        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 8, 1), 1000m, 700m, 300m, top),
            PnlTestData.Row(new DateOnly(2026, 8, 1), 1000m, 700m, 400m, middle),
            PnlTestData.Row(new DateOnly(2026, 8, 1), 1000m, 700m, 600m, bottom),
        };

        Mock<IProductSkuLookup> lookup = PnlTestData.Lookup();
        lookup
            .Setup(l => l.GetSkusAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>
            {
                [top] = "SKU-TOP",
                [middle] = "SKU-MIDDLE",
                [bottom] = "SKU-BOTTOM",
            });

        GetPnlProductTableQueryHandler handler = new GetPnlProductTableQueryHandler(
            new PnlReadStoreFake(new PnlLinePage(), rows),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object,
            lookup.Object);

        // Act
        PnlProductTableResponse response = await handler.Handle(
            new GetPnlProductTableQuery
            {
                Filter = new PnlQueryFilter
                {
                    From = new DateOnly(2026, 8, 1),
                    To = new DateOnly(2026, 8, 31),
                },
                SortBy = PnlProductSortKey.Margin,
                Descending = true,
                PageSize = 1,
            },
            CancellationToken.None);

        // Assert
        response.Items.Should().ContainSingle().Which.ProductId.Should().Be(top);
        response.Items[0].Sku.Should().Be("SKU-TOP");
        response.TotalCount.Should().Be(3);

        lookup.Verify(
            l => l.GetSkusAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(top)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetProductTable_WithSearch_LabelsTheWholePopulationBeforeFiltering()
    {
        // Arrange: a search is matched against SKUs, so every product needs its label first.
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        Guid third = Guid.NewGuid();

        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 8, 1), 1000m, 700m, 300m, first),
            PnlTestData.Row(new DateOnly(2026, 8, 1), 500m, 450m, 600m, second),
            PnlTestData.Row(new DateOnly(2026, 8, 1), 300m, 300m, cost: null, productId: third),
        };

        Mock<IProductSkuLookup> lookup = PnlTestData.Lookup();
        lookup
            .Setup(l => l.GetSkusAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>
            {
                [first] = "BAG-CLASSIC-BLK",
                [second] = "WALLET-SLIM-BRN",
                [third] = "BELT-35-BLK",
            });

        GetPnlProductTableQueryHandler handler = new GetPnlProductTableQueryHandler(
            new PnlReadStoreFake(new PnlLinePage(), rows),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object,
            lookup.Object);

        // Act
        PnlProductTableResponse response = await handler.Handle(
            new GetPnlProductTableQuery
            {
                Filter = new PnlQueryFilter
                {
                    From = new DateOnly(2026, 8, 1),
                    To = new DateOnly(2026, 8, 31),
                },
                Search = "  bag  ",
            },
            CancellationToken.None);

        // Assert
        response.Items.Should().ContainSingle()
            .Which.Sku.Should().Be("BAG-CLASSIC-BLK");
        response.Search.Should().Be("bag");
        response.TotalCount.Should().Be(1);

        lookup.Verify(
            l => l.GetSkusAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 3),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetProductTable_SortedBySku_LabelsTheWholePopulation()
    {
        // Arrange: the order itself is built from labels, so a page-only lookup is not enough.
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();

        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 8, 1), 1000m, 700m, 300m, first),
            PnlTestData.Row(new DateOnly(2026, 8, 1), 500m, 450m, 600m, second),
        };

        Mock<IProductSkuLookup> lookup = PnlTestData.Lookup();
        lookup
            .Setup(l => l.GetSkusAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>
            {
                [first] = "SKU-B",
                [second] = "SKU-A",
            });

        GetPnlProductTableQueryHandler handler = new GetPnlProductTableQueryHandler(
            new PnlReadStoreFake(new PnlLinePage(), rows),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object,
            lookup.Object);

        // Act
        PnlProductTableResponse response = await handler.Handle(
            new GetPnlProductTableQuery
            {
                Filter = new PnlQueryFilter
                {
                    From = new DateOnly(2026, 8, 1),
                    To = new DateOnly(2026, 8, 31),
                },
                SortBy = PnlProductSortKey.Sku,
                Descending = false,
                PageSize = 1,
            },
            CancellationToken.None);

        // Assert
        response.Items.Should().ContainSingle().Which.Sku.Should().Be("SKU-A");

        lookup.Verify(
            l => l.GetSkusAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetProductTable_EchoesTheRequestAndReportsTheServedPage()
    {
        // Arrange: a page far beyond the last one is clamped, and the echo says which page ran.
        PnlReadStoreFake store = new PnlReadStoreFake(new PnlLinePage(), new List<PnlLineRow>())
        {
            CurrencyExclusions = new List<PnlCurrencyExclusion>
            {
                new() { Currency = "KZT", Lines = 54, RevenueAmount = 512000m },
                new() { Currency = "BYN", Lines = 8, RevenueAmount = 410m },
            },
        };

        GetPnlProductTableQueryHandler handler = new GetPnlProductTableQueryHandler(
            store,
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object,
            PnlTestData.Lookup().Object);

        // Act
        PnlProductTableResponse response = await handler.Handle(
            new GetPnlProductTableQuery
            {
                Filter = new PnlQueryFilter
                {
                    From = new DateOnly(2026, 8, 1),
                    To = new DateOnly(2026, 8, 31),
                },
                Segment = PnlProductSegment.Loss,
                Search = " bag ",
                SortBy = PnlProductSortKey.ReturnOnSoldCostPercent,
                Descending = false,
                Page = 5,
                PageSize = 50,
            },
            CancellationToken.None);

        // Assert
        response.Page.Should().Be(1);
        response.PageSize.Should().Be(50);
        response.Segment.Should().Be(PnlProductSegment.Loss);
        response.Search.Should().Be("bag");
        response.SortBy.Should().Be(PnlProductSortKey.ReturnOnSoldCostPercent);
        response.Descending.Should().BeFalse();
        response.ExcludedByCurrency.Should().Be(62);
        response.ExcludedCurrencies.Should().HaveCount(2);
    }
}
