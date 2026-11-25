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
/// P&amp;L product details: the product sheet, its channel shares and the document reconciliation.
/// </summary>
public class GetPnlProductDetailsQueryHandlerTests
{
    [Fact]
    public async Task GetProductDetails_ProductWithoutLines_AnswersAnEmptySheet()
    {
        GetPnlProductDetailsQueryHandler handler = new GetPnlProductDetailsQueryHandler(
            new PnlReadStoreFake(new PnlLinePage(), new List<PnlLineRow>()),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object,
            PnlTestData.Lookup().Object);

        PnlProductDetailsResponse response = await handler.Handle(
            new GetPnlProductDetailsQuery
            {
                ProductId = Guid.NewGuid(),
                Filter = new PnlQueryFilter
                {
                    From = new DateOnly(2026, 8, 1),
                    To = new DateOnly(2026, 8, 31),
                },
            },
            CancellationToken.None);

        response.Product.Should().BeNull();
        response.Channels.Should().BeEmpty();
        response.Documents.Pending.Lines.Should().Be(0);
        response.Documents.Overdue.Lines.Should().Be(0);
        response.Documents.ControlDifferenceAmount.Should().BeNull();
        response.Currency.Should().Be("RUB");
    }

    [Fact]
    public async Task GetProductDetails_ChannelsAreOrderedAndTheirSharesAddUpToHundred()
    {
        // Arrange: one product sold through two channels, the first earning three times more.
        Guid productId = Guid.NewGuid();
        Guid firstChannel = Guid.NewGuid();
        Guid secondChannel = Guid.NewGuid();

        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 8, 1), 300m, 270m, 100m, productId, channelId: firstChannel),
            PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 20m, productId, channelId: secondChannel),
        };

        GetPnlProductDetailsQueryHandler handler = new GetPnlProductDetailsQueryHandler(
            new PnlReadStoreFake(new PnlLinePage(), rows),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object,
            PnlTestData.Lookup().Object);

        // Act
        PnlProductDetailsResponse response = await handler.Handle(
            new GetPnlProductDetailsQuery
            {
                ProductId = productId,
                Filter = new PnlQueryFilter
                {
                    From = new DateOnly(2026, 8, 1),
                    To = new DateOnly(2026, 8, 31),
                },
            },
            CancellationToken.None);

        // Assert
        response.Product.Should().NotBeNull();
        response.Product!.RevenueAmount.Should().Be(400m);

        response.Channels.Should().HaveCount(2);
        response.Channels[0].ChannelId.Should().Be(firstChannel);
        response.Channels[0].RevenueSharePercent.Should().Be(75m);
        response.Channels[1].ChannelId.Should().Be(secondChannel);
        response.Channels[1].RevenueSharePercent.Should().Be(25m);
        response.Channels.Sum(channel => channel.RevenueSharePercent!.Value).Should().Be(100m);
    }

    [Fact]
    public async Task GetProductDetails_DocumentsReconcileWithTheProductRow()
    {
        // Arrange: one complete, one provisional and one overdue line of the same product.
        Guid productId = Guid.NewGuid();

        PnlLineRow complete = PnlTestData.Row(
            new DateOnly(2026, 8, 1),
            100m,
            90m,
            10m,
            productId,
            completeness: PnlLineCompleteness.Complete);

        PnlLineRow provisional = PnlTestData.Row(
            new DateOnly(2026, 8, 1),
            200m,
            180m,
            20m,
            productId,
            completeness: PnlLineCompleteness.Provisional);
        provisional.MissingDocumentTypes = new List<string> { "Commission" };
        provisional.ExpectedBy = new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);

        PnlLineRow overdue = PnlTestData.Row(
            new DateOnly(2026, 8, 1),
            300m,
            270m,
            30m,
            productId,
            completeness: PnlLineCompleteness.Overdue);
        overdue.MissingDocumentTypes = new List<string> { "Acquiring" };
        overdue.ExpectedBy = new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);

        GetPnlProductDetailsQueryHandler handler = new GetPnlProductDetailsQueryHandler(
            new PnlReadStoreFake(new PnlLinePage(), new List<PnlLineRow> { complete, provisional, overdue }),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object,
            PnlTestData.Lookup().Object);

        // Act
        PnlProductDetailsResponse response = await handler.Handle(
            new GetPnlProductDetailsQuery
            {
                ProductId = productId,
                Filter = new PnlQueryFilter
                {
                    From = new DateOnly(2026, 8, 1),
                    To = new DateOnly(2026, 8, 31),
                    Completeness = "all",
                },
            },
            CancellationToken.None);

        // Assert
        response.Product.Should().NotBeNull();
        response.Product!.ProvisionalRevenueAmount.Should().Be(500m);
        response.Product.OverdueRevenueAmount.Should().Be(300m);

        response.Documents.Pending.Lines.Should().Be(1);
        response.Documents.Pending.RevenueAmount.Should().Be(200m);
        response.Documents.Overdue.Lines.Should().Be(1);
        response.Documents.Overdue.RevenueAmount.Should().Be(300m);

        (response.Documents.Pending.RevenueAmount + response.Documents.Overdue.RevenueAmount)
            .Should().Be(response.Product.ProvisionalRevenueAmount);
        response.Documents.Overdue.RevenueAmount.Should().Be(response.Product.OverdueRevenueAmount);
        response.Documents.ControlDifferenceAmount.Should().Be(response.Product.ControlDifferenceAmount);
        response.Documents.LinesWithControlDifference.Should().Be(0);
    }
}
