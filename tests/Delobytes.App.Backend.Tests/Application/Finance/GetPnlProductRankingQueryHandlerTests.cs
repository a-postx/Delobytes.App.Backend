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
/// P&amp;L product ranking: the ranked rows and the metrics they expose.
/// </summary>
public class GetPnlProductRankingQueryHandlerTests
{
    [Fact]
    public async Task GetProductRanking_ReturnsTopRowWithRankAndLabelsOnlyThatProduct()
    {
        // Arrange: two products, the second earns more margin, so a slice of one must hold it.
        Guid topId = Guid.NewGuid();
        Guid lowId = Guid.NewGuid();

        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, lowId),
            PnlTestData.Row(new DateOnly(2026, 8, 1), 300m, 270m, 30m, topId),
        };

        Mock<IProductSkuLookup> lookup = PnlTestData.Lookup();
        lookup
            .Setup(l => l.GetSkusAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [topId] = "SKU-TOP" });

        GetPnlProductRankingQueryHandler handler = new GetPnlProductRankingQueryHandler(
            new PnlReadStoreFake(new PnlLinePage(), rows),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object,
            lookup.Object);

        // Act
        PnlProductRankingResponse response = await handler.Handle(
            new GetPnlProductRankingQuery { Take = 1, Coverage = PnlRankingCoverage.Any },
            CancellationToken.None);

        // Assert
        response.Rows.Should().ContainSingle();
        response.Rows[0].ProductId.Should().Be(topId);
        response.Rows[0].Sku.Should().Be("SKU-TOP");
        response.Rows[0].Rank.Should().Be(1);
        response.RankedRows.Should().Be(2);
        response.TotalRows.Should().Be(2);
        response.Take.Should().Be(1);
        lookup.Verify(
            l => l.GetSkusAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(topId)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetProductRanking_RowWithoutMargin_IsNotRanked()
    {
        // Arrange: the line without a cost fact has no margin, so it cannot take a place. Distinct
        // product identifiers keep the two lines in separate groups; an unlinked pair would fold
        // into one "no product" group.
        Guid knownId = Guid.NewGuid();
        Guid unknownId = Guid.NewGuid();

        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, knownId),
            PnlTestData.Row(new DateOnly(2026, 8, 1), 1000m, 900m, cost: null, productId: unknownId),
        };

        GetPnlProductRankingQueryHandler handler = new GetPnlProductRankingQueryHandler(
            new PnlReadStoreFake(new PnlLinePage(), rows),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object,
            PnlTestData.Lookup().Object);

        // Act
        PnlProductRankingResponse response = await handler.Handle(
            new GetPnlProductRankingQuery { Take = 10, Coverage = PnlRankingCoverage.Any },
            CancellationToken.None);

        // Assert
        response.Rows.Should().ContainSingle().Which.Margin.Should().Be(80m);
        response.RankedRows.Should().Be(1);
        response.TotalRows.Should().Be(2);
    }

    [Fact]
    public async Task GetProductRanking_ManyGroups_IsNotLimitedByTheSummaryResponseBudget()
    {
        // Arrange: the ranking shares the fold but returns only its top slice, so the summary's
        // response budget must not reject it.
        List<PnlLineRow> rows = new();

        for (int index = 0; index <= GetPnlSummaryQueryHandler.MaxResponseRows; index++)
        {
            rows.Add(PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, Guid.NewGuid()));
        }

        GetPnlProductRankingQueryHandler handler = new GetPnlProductRankingQueryHandler(
            new PnlReadStoreFake(new PnlLinePage(), rows),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object,
            PnlTestData.Lookup().Object);

        // Act
        PnlProductRankingResponse response = await handler.Handle(
            new GetPnlProductRankingQuery { Take = 50, Coverage = PnlRankingCoverage.Any },
            CancellationToken.None);

        // Assert
        response.Rows.Should().HaveCount(50);
        response.TotalRows.Should().Be(GetPnlSummaryQueryHandler.MaxResponseRows + 1);
    }
}
