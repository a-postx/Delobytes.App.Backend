using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Domain.Enums;
using Delobytes.App.Backend.Finance.Infrastructure.Persistence;
using Delobytes.App.Backend.Finance.Infrastructure.Persistence.Stores;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure.Finance;

/// <summary>
/// Behaviour of the P&amp;L read store against real DbContexts: the joins, the filters, the keyset
/// pages and the currency diagnostic must agree with each other.
/// </summary>
public class PnlReadStoreTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ChannelId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherChannelId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ProductId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static readonly DateOnly Day1 = new(2026, 3, 1);
    private static readonly DateOnly Day2 = new(2026, 3, 2);
    private static readonly DateOnly Day3 = new(2026, 3, 3);

    [Fact]
    public async Task GetLinesAsync_PagesThroughEveryRowOnce()
    {
        string database = NewDatabaseName();
        Guid[] orderLines = await SeedLinesAsync(database);

        using FinanceDbContext context = CreateContext(database, TenantId);
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext(TenantId));

        PnlLinePage first = await store.GetLinesAsync("RUB", new PnlLineFilter(), 2, null, CancellationToken.None);

        first.Rows.Should().HaveCount(2);
        first.NextToken.Should().NotBeNull();

        PnlLinePage second = await store.GetLinesAsync(
            "RUB", new PnlLineFilter(), 2, first.NextToken, CancellationToken.None);

        second.Rows.Should().HaveCount(1);
        second.NextToken.Should().BeNull();

        first.Rows.Select(r => r.OrderLineId)
            .Concat(second.Rows.Select(r => r.OrderLineId))
            .Should().Equal(orderLines);
    }

    [Fact]
    public async Task GetLinesAsync_ExactPageSize_HasNoEmptyTrailingPage()
    {
        string database = NewDatabaseName();
        await SeedLinesAsync(database);

        using FinanceDbContext context = CreateContext(database, TenantId);
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext(TenantId));

        PnlLinePage page = await store.GetLinesAsync("RUB", new PnlLineFilter(), 3, null, CancellationToken.None);

        page.Rows.Should().HaveCount(3);
        page.NextToken.Should().BeNull();
    }

    [Fact]
    public async Task GetLinesAsync_FiltersByChannel()
    {
        string database = NewDatabaseName();
        await SeedLinesAsync(database);

        using FinanceDbContext context = CreateContext(database, TenantId);
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext(TenantId));

        PnlLinePage page = await store.GetLinesAsync(
            "RUB", new PnlLineFilter { ChannelId = ChannelId }, 100, null, CancellationToken.None);

        page.Rows.Should().HaveCount(2);
        page.Rows.Should().OnlyContain(r => r.ChannelId == ChannelId);
    }

    [Fact]
    public async Task GetLinesAsync_FiltersByProductAndDropsLinesWithoutCost()
    {
        string database = NewDatabaseName();
        await SeedLinesAsync(database);

        using FinanceDbContext context = CreateContext(database, TenantId);
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext(TenantId));

        PnlLinePage page = await store.GetLinesAsync(
            "RUB", new PnlLineFilter { ProductId = ProductId }, 100, null, CancellationToken.None);

        page.Rows.Should().HaveCount(1);
        page.Rows[0].ProductId.Should().Be(ProductId);
        page.Rows[0].CostStatus.Should().Be(OrderLineCostStatus.Resolved);
    }

    [Fact]
    public async Task GetLinesAsync_UnresolvedCost_LeavesProductIdNullAndKeepsChannelProductId()
    {
        string database = NewDatabaseName();
        Guid orderLineId = Guid.Parse("00000005-0000-0000-0000-000000000005");
        Guid channelProductId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        using (FinanceDbContext seed = CreateContext(database, TenantId))
        {
            seed.PnlLines.Add(Line(Guid.NewGuid(), orderLineId, "RUB", Day1));
            seed.OrderLineRefs.Add(Reference(orderLineId, ChannelId, channelProductId));
            seed.OrderLineCosts.Add(UnresolvedCost(orderLineId));

            await seed.SaveChangesAsync();
        }

        using FinanceDbContext context = CreateContext(database, TenantId);
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext(TenantId));

        PnlLinePage page = await store.GetLinesAsync("RUB", new PnlLineFilter(), 100, null, CancellationToken.None);

        PnlLineRow row = page.Rows.Single(r => r.OrderLineId == orderLineId);

        // The channel product is not a product: substituting it would hide the line from the
        // diagnostics and put it into a group no catalogue product can be joined to.
        row.ProductId.Should().BeNull();
        row.ChannelProductId.Should().Be(channelProductId);
        row.CostStatus.Should().Be(OrderLineCostStatus.ProductNotLinked);
    }

    [Fact]
    public async Task GetLinesAsync_WithoutCostFact_LeavesProductIdNullAndKeepsChannelProductId()
    {
        string database = NewDatabaseName();
        Guid orderLineId = Guid.Parse("00000008-0000-0000-0000-000000000008");
        Guid channelProductId = Guid.Parse("abababab-abab-abab-abab-abababababab");

        using (FinanceDbContext seed = CreateContext(database, TenantId))
        {
            seed.PnlLines.Add(Line(Guid.NewGuid(), orderLineId, "RUB", Day1));
            seed.OrderLineRefs.Add(Reference(orderLineId, ChannelId, channelProductId));

            await seed.SaveChangesAsync();
        }

        using FinanceDbContext context = CreateContext(database, TenantId);
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext(TenantId));

        PnlLinePage page = await store.GetLinesAsync("RUB", new PnlLineFilter(), 100, null, CancellationToken.None);

        PnlLineRow row = page.Rows.Single(r => r.OrderLineId == orderLineId);

        row.ProductId.Should().BeNull();
        row.ChannelProductId.Should().Be(channelProductId);
        row.CostStatus.Should().BeNull();
    }

    [Fact]
    public async Task GetLinesAsync_FiltersByChannelProductIdIndependentlyOfTheProduct()
    {
        string database = NewDatabaseName();
        Guid firstLine = Guid.Parse("00000009-0000-0000-0000-000000000009");
        Guid secondLine = Guid.Parse("0000000a-0000-0000-0000-00000000000a");
        Guid channelProductId = Guid.Parse("cdcdcdcd-cdcd-cdcd-cdcd-cdcdcdcdcdcd");

        using (FinanceDbContext seed = CreateContext(database, TenantId))
        {
            seed.PnlLines.Add(Line(Guid.NewGuid(), firstLine, "RUB", Day1));
            seed.PnlLines.Add(Line(Guid.NewGuid(), secondLine, "RUB", Day1));
            seed.OrderLineRefs.Add(Reference(firstLine, ChannelId, channelProductId));
            seed.OrderLineRefs.Add(Reference(secondLine, ChannelId, Guid.NewGuid()));

            await seed.SaveChangesAsync();
        }

        using FinanceDbContext context = CreateContext(database, TenantId);
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext(TenantId));

        PnlLinePage page = await store.GetLinesAsync(
            "RUB",
            new PnlLineFilter { ChannelProductId = channelProductId },
            100,
            null,
            CancellationToken.None);

        page.Rows.Should().ContainSingle();
        page.Rows[0].OrderLineId.Should().Be(firstLine);
    }

    [Fact]
    public async Task GetLinesAsync_PrefersResolvedCostProductId_OverChannelProductId()
    {
        string database = NewDatabaseName();
        Guid orderLineId = Guid.Parse("00000006-0000-0000-0000-000000000006");
        Guid channelProductId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        Guid resolvedProductId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

        using (FinanceDbContext seed = CreateContext(database, TenantId))
        {
            seed.PnlLines.Add(Line(Guid.NewGuid(), orderLineId, "RUB", Day1));
            seed.OrderLineRefs.Add(Reference(orderLineId, ChannelId, channelProductId));
            seed.OrderLineCosts.Add(Cost(orderLineId, resolvedProductId));

            await seed.SaveChangesAsync();
        }

        using FinanceDbContext context = CreateContext(database, TenantId);
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext(TenantId));

        PnlLinePage page = await store.GetLinesAsync("RUB", new PnlLineFilter(), 100, null, CancellationToken.None);

        page.Rows.Single(r => r.OrderLineId == orderLineId).ProductId.Should().Be(resolvedProductId);
    }

    [Fact]
    public async Task GetLinesAsync_WithoutCostOrChannelProductId_LeavesProductIdNull()
    {
        string database = NewDatabaseName();
        Guid orderLineId = Guid.Parse("00000007-0000-0000-0000-000000000007");

        using (FinanceDbContext seed = CreateContext(database, TenantId))
        {
            seed.PnlLines.Add(Line(Guid.NewGuid(), orderLineId, "RUB", Day1));
            seed.OrderLineRefs.Add(Reference(orderLineId, ChannelId));

            await seed.SaveChangesAsync();
        }

        using FinanceDbContext context = CreateContext(database, TenantId);
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext(TenantId));

        PnlLinePage page = await store.GetLinesAsync("RUB", new PnlLineFilter(), 100, null, CancellationToken.None);

        page.Rows.Single(r => r.OrderLineId == orderLineId).ProductId.Should().BeNull();
    }

    [Fact]
    public async Task GetLinesAsync_FiltersByDateRangeAndState()
    {
        string database = NewDatabaseName();
        await SeedLinesAsync(database);

        using FinanceDbContext context = CreateContext(database, TenantId);
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext(TenantId));

        PnlLinePage page = await store.GetLinesAsync(
            "RUB",
            new PnlLineFilter { From = Day2, To = Day3, State = PnlLineState.NotCalculated },
            100,
            null,
            CancellationToken.None);

        page.Rows.Should().HaveCount(1);
        page.Rows[0].SaleDate.Should().Be(Day2);
    }

    [Fact]
    public async Task GetLinesAsync_LineWithoutSale_IsFilteredByItsFinancialPeriod()
    {
        // A non-buyout has no sale date: its only date is the period of the documents behind it.
        string database = NewDatabaseName();
        Guid orderLineId = Guid.Parse("00000010-0000-0000-0000-000000000010");

        using (FinanceDbContext seed = CreateContext(database, TenantId))
        {
            PnlLine nonBuyout = Line(Guid.NewGuid(), orderLineId, "RUB", saleDate: null);
            nonBuyout.FinancialPeriodFrom = Day2;
            nonBuyout.FinancialPeriodTo = Day2;
            nonBuyout.RevenueAmount = null;
            nonBuyout.NetRevenueAmount = -53.84m;
            nonBuyout.DeliveryFeeAmount = -53.84m;

            seed.PnlLines.Add(nonBuyout);
            seed.OrderLineRefs.Add(Reference(orderLineId, ChannelId));

            await seed.SaveChangesAsync();
        }

        using FinanceDbContext context = CreateContext(database, TenantId);
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext(TenantId));

        PnlLinePage inside = await store.GetLinesAsync(
            "RUB", new PnlLineFilter { From = Day2, To = Day2 }, 100, null, CancellationToken.None);

        PnlLineRow row = inside.Rows.Should().ContainSingle().Subject;
        row.SaleDate.Should().BeNull();
        row.PeriodDate.Should().Be(Day2);
        row.NetRevenueAmount.Should().Be(-53.84m);

        PnlLinePage outside = await store.GetLinesAsync(
            "RUB", new PnlLineFilter { From = Day3, To = Day3 }, 100, null, CancellationToken.None);

        outside.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLinesAsync_ExcludedByCurrency_AppliesTheOtherFilters()
    {
        string database = NewDatabaseName();
        await SeedLinesAsync(database);

        using FinanceDbContext context = CreateContext(database, TenantId);
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext(TenantId));

        PnlLinePage page = await store.GetLinesAsync(
            "RUB",
            new PnlLineFilter { From = Day3, To = Day3 },
            100,
            null,
            CancellationToken.None);

        page.Rows.Should().HaveCount(1);
        page.ExcludedByCurrency.Should().Be(1);
    }

    [Fact]
    public async Task GetLinesAsync_FiltersByCompletenessStates()
    {
        string database = NewDatabaseName();
        Guid completeOrderLine = Guid.Parse("0000000b-0000-0000-0000-00000000000b");
        Guid provisionalOrderLine = Guid.Parse("0000000c-0000-0000-0000-00000000000c");
        Guid overdueOrderLine = Guid.Parse("0000000d-0000-0000-0000-00000000000d");
        Guid exceptionOrderLine = Guid.Parse("0000000e-0000-0000-0000-00000000000e");

        using (FinanceDbContext seed = CreateContext(database, TenantId))
        {
            seed.PnlLines.AddRange(
                Line(Guid.NewGuid(), completeOrderLine, "RUB", Day1),
                Line(Guid.NewGuid(), provisionalOrderLine, "RUB", Day1, completeness: PnlLineCompleteness.Provisional),
                Line(Guid.NewGuid(), overdueOrderLine, "RUB", Day1, completeness: PnlLineCompleteness.Overdue),
                Line(Guid.NewGuid(), exceptionOrderLine, "RUB", Day1, completeness: PnlLineCompleteness.ClosedWithException));

            seed.OrderLineRefs.AddRange(
                Reference(completeOrderLine, ChannelId),
                Reference(provisionalOrderLine, ChannelId),
                Reference(overdueOrderLine, ChannelId),
                Reference(exceptionOrderLine, ChannelId));

            await seed.SaveChangesAsync();
        }

        using FinanceDbContext context = CreateContext(database, TenantId);
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext(TenantId));

        PnlLinePage page = await store.GetLinesAsync(
            "RUB",
            new PnlLineFilter
            {
                CompletenessStates = new[]
                {
                    PnlLineCompleteness.Complete,
                    PnlLineCompleteness.ClosedWithException,
                },
            },
            100,
            null,
            CancellationToken.None);

        page.Rows.Select(r => r.OrderLineId).Should().BeEquivalentTo(new[] { completeOrderLine, exceptionOrderLine });
    }

    [Fact]
    public async Task GetLinesAsync_WithoutCompletenessStates_ReadsEveryState()
    {
        string database = NewDatabaseName();
        Guid orderLineId = Guid.Parse("0000000f-0000-0000-0000-00000000000f");

        using (FinanceDbContext seed = CreateContext(database, TenantId))
        {
            seed.PnlLines.Add(Line(
                Guid.NewGuid(), orderLineId, "RUB", Day1, completeness: PnlLineCompleteness.ClosedWithException));
            seed.OrderLineRefs.Add(Reference(orderLineId, ChannelId));

            await seed.SaveChangesAsync();
        }

        using FinanceDbContext context = CreateContext(database, TenantId);
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext(TenantId));

        PnlLinePage page = await store.GetLinesAsync("RUB", new PnlLineFilter(), 100, null, CancellationToken.None);

        page.Rows.Should().ContainSingle().Which.OrderLineId.Should().Be(orderLineId);
    }

    [Fact]
    public async Task GetLinesAsync_DoesNotReadOtherTenants()
    {
        string database = NewDatabaseName();
        await SeedLinesAsync(database);

        using FinanceDbContext context = CreateContext(database, OtherTenantId);
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext(OtherTenantId));

        PnlLinePage page = await store.GetLinesAsync("RUB", new PnlLineFilter(), 100, null, CancellationToken.None);

        page.Rows.Should().BeEmpty();
        page.ExcludedByCurrency.Should().Be(0);
    }

    [Fact]
    public async Task GetLinesAsync_WithoutResolvedTenant_Throws()
    {
        string database = NewDatabaseName();

        using FinanceDbContext context = CreateContext(database, TenantId);
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext(Guid.Empty));

        Func<Task> act = () => store.GetLinesAsync("RUB", new PnlLineFilter(), 10, null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StreamForSummaryAsync_AppliesChannelAndProductFiltersBeforeStreaming()
    {
        string database = NewDatabaseName();
        Guid[] orderLines = await SeedLinesAsync(database);

        using FinanceDbContext context = CreateContext(database, TenantId);
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext(TenantId));

        List<PnlLineRow> rows = new();

        await foreach (PnlLineRow row in store.StreamForSummaryAsync(
            "RUB",
            new PnlLineFilter { ChannelId = ChannelId, ProductId = ProductId },
            CancellationToken.None))
        {
            rows.Add(row);
        }

        rows.Should().HaveCount(1);
        rows[0].OrderLineId.Should().Be(orderLines[0]);
    }

    [Fact]
    public async Task StreamForSummaryAsync_WithoutResolvedTenant_ThrowsBeforeTheFirstRow()
    {
        // The tenant must fail the call itself, not the caller's loop: a stream that throws halfway
        // would let a report run against the wrong scope for as long as it takes to notice.
        string database = NewDatabaseName();
        await SeedLinesAsync(database);

        using FinanceDbContext context = CreateContext(database, TenantId);
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext(Guid.Empty));

        Func<Task> act = async () =>
        {
            await foreach (PnlLineRow row in store.StreamForSummaryAsync(
                "RUB", new PnlLineFilter(), CancellationToken.None))
            {
                _ = row;
            }
        };

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetLinesAsync_NonPositivePageSize_Throws()
    {
        string database = NewDatabaseName();

        using FinanceDbContext context = CreateContext(database, TenantId);
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext(TenantId));

        Func<Task> act = () => store.GetLinesAsync("RUB", new PnlLineFilter(), 0, null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task GetDiagnosticsAsync_AttributesEntriesByLineIdentity()
    {
        string database = NewDatabaseName();
        Guid orderLineId = Guid.Parse("00000001-0000-0000-0000-000000000001");

        using (FinanceDbContext seed = CreateContext(database, TenantId))
        {
            seed.OrderLineRefs.Add(ReferenceWith(orderLineId, ChannelId, "srid-1", "order-1", "sku-1", DateTimeOffset.UnixEpoch));
            seed.PnlEntries.AddRange(
                Entry(ChannelId, "srid-1", null, "sku-1", Day1),
                Entry(ChannelId, "srid-404", null, "sku-1", Day1));

            await seed.SaveChangesAsync();
        }

        using FinanceDbContext context = CreateContext(database, TenantId);
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext(TenantId));

        PnlDiagnosticsResult diagnostics = await store.GetDiagnosticsAsync(Day1, Day1, CancellationToken.None);

        diagnostics.UnattributedEntries.Should().Be(1);
        diagnostics.AmbiguousAttributions.Should().Be(0);
    }

    [Fact]
    public async Task GetDiagnosticsAsync_DoesNotNarrowCandidatesByOrderDate()
    {
        string database = NewDatabaseName();
        Guid orderLineId = Guid.Parse("00000001-0000-0000-0000-000000000001");

        using (FinanceDbContext seed = CreateContext(database, TenantId))
        {
            // The order line was placed long before the period: attribution keys off identities, so
            // narrowing the candidates by OrderDate would silently lose the match.
            seed.OrderLineRefs.Add(ReferenceWith(
                orderLineId, ChannelId, "srid-1", "order-1", "sku-1", new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)));
            seed.PnlEntries.Add(Entry(ChannelId, "srid-1", null, "sku-1", Day1));

            await seed.SaveChangesAsync();
        }

        using FinanceDbContext context = CreateContext(database, TenantId);
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext(TenantId));

        PnlDiagnosticsResult diagnostics = await store.GetDiagnosticsAsync(Day1, Day1, CancellationToken.None);

        diagnostics.UnattributedEntries.Should().Be(0);
    }

    [Fact]
    public async Task GetDiagnosticsAsync_MatchesAccrualWithoutLineIdentityByOrderKey()
    {
        string database = NewDatabaseName();
        Guid orderLineId = Guid.Parse("00000001-0000-0000-0000-000000000001");

        using (FinanceDbContext seed = CreateContext(database, TenantId))
        {
            seed.OrderLineRefs.Add(ReferenceWith(orderLineId, ChannelId, "srid-1", "order-1", "sku-1", DateTimeOffset.UnixEpoch));
            seed.PnlEntries.Add(Entry(ChannelId, null, "order-1", "sku-1", Day1));

            await seed.SaveChangesAsync();
        }

        using FinanceDbContext context = CreateContext(database, TenantId);
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext(TenantId));

        PnlDiagnosticsResult diagnostics = await store.GetDiagnosticsAsync(Day1, Day1, CancellationToken.None);

        diagnostics.UnattributedEntries.Should().Be(0);
    }

    [Fact]
    public async Task GetDiagnosticsAsync_WithoutEntries_ReportsNoWork()
    {
        string database = NewDatabaseName();

        using (FinanceDbContext seed = CreateContext(database, TenantId))
        {
            seed.OrderLineRefs.Add(Reference(Guid.NewGuid(), ChannelId));
            await seed.SaveChangesAsync();
        }

        using FinanceDbContext context = CreateContext(database, TenantId);
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext(TenantId));

        PnlDiagnosticsResult diagnostics = await store.GetDiagnosticsAsync(Day1, Day3, CancellationToken.None);

        diagnostics.UnattributedEntries.Should().Be(0);
        diagnostics.ControlDifference.Should().Be(0m);
    }

    [Fact]
    public async Task GetDiagnosticsAsync_SumsOpenControlDifferences()
    {
        string database = NewDatabaseName();

        using (FinanceDbContext seed = CreateContext(database, TenantId))
        {
            PnlLine balanced = Line(Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.NewGuid(), "RUB", Day1);
            balanced.ControlDifferenceAmount = 0m;

            PnlLine open = Line(Guid.Parse("00000000-0000-0000-0000-000000000002"), Guid.NewGuid(), "RUB", Day2);
            open.ControlDifferenceAmount = 3.5m;

            seed.PnlLines.AddRange(balanced, open);
            await seed.SaveChangesAsync();
        }

        using FinanceDbContext context = CreateContext(database, TenantId);
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext(TenantId));

        PnlDiagnosticsResult diagnostics = await store.GetDiagnosticsAsync(Day1, Day3, CancellationToken.None);

        diagnostics.ControlDifference.Should().Be(3.5m);
        diagnostics.LinesWithOpenControlDifference.Should().Be(1);
    }

    [Fact]
    public async Task GetDiagnosticsAsync_CountsAndSumsLinesWithoutProductLink()
    {
        string database = NewDatabaseName();
        Guid linkedOrderLine = Guid.Parse("00000008-0000-0000-0000-000000000008");
        Guid unlinkedOrderLine = Guid.Parse("00000009-0000-0000-0000-000000000009");

        using (FinanceDbContext seed = CreateContext(database, TenantId))
        {
            PnlLine linked = Line(Guid.NewGuid(), linkedOrderLine, "RUB", Day1);
            PnlLine unlinked = Line(Guid.NewGuid(), unlinkedOrderLine, "RUB", Day1);
            unlinked.RevenueAmount = 150m;

            seed.PnlLines.AddRange(linked, unlinked);
            seed.OrderLineRefs.AddRange(
                Reference(linkedOrderLine, ChannelId),
                Reference(unlinkedOrderLine, ChannelId));
            seed.OrderLineCosts.Add(Cost(linkedOrderLine, ProductId));

            await seed.SaveChangesAsync();
        }

        using FinanceDbContext context = CreateContext(database, TenantId);
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext(TenantId));

        PnlDiagnosticsResult diagnostics = await store.GetDiagnosticsAsync(Day1, Day1, CancellationToken.None);

        diagnostics.LinesWithoutProductLink.Should().Be(1);
        diagnostics.UnlinkedRevenueAmount.Should().Be(150m);
    }

    [Fact]
    public async Task GetDiagnosticsAsync_WithoutUnlinkedLines_ReportsZero()
    {
        string database = NewDatabaseName();
        Guid orderLineId = Guid.Parse("0000000a-0000-0000-0000-00000000000a");

        using (FinanceDbContext seed = CreateContext(database, TenantId))
        {
            seed.PnlLines.Add(Line(Guid.NewGuid(), orderLineId, "RUB", Day1));
            seed.OrderLineRefs.Add(Reference(orderLineId, ChannelId));
            seed.OrderLineCosts.Add(Cost(orderLineId, ProductId));

            await seed.SaveChangesAsync();
        }

        using FinanceDbContext context = CreateContext(database, TenantId);
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext(TenantId));

        PnlDiagnosticsResult diagnostics = await store.GetDiagnosticsAsync(Day1, Day1, CancellationToken.None);

        diagnostics.LinesWithoutProductLink.Should().Be(0);
        diagnostics.UnlinkedRevenueAmount.Should().Be(0m);
    }

    [Fact]
    public void ParseCursor_RejectsMalformedToken()
    {
        Action act = () => PnlReadStore.ParseCursor("not-a-guid");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ParseCursor_TreatsBlankTokenAsFirstPage()
    {
        PnlReadStore.ParseCursor(null).Should().BeNull();
        PnlReadStore.ParseCursor("  ").Should().BeNull();
    }

    private static async Task<Guid[]> SeedLinesAsync(string database)
    {
        Guid firstOrderLine = Guid.Parse("00000001-0000-0000-0000-000000000001");
        Guid secondOrderLine = Guid.Parse("00000002-0000-0000-0000-000000000002");
        Guid thirdOrderLine = Guid.Parse("00000003-0000-0000-0000-000000000003");
        Guid otherCurrencyOrderLine = Guid.Parse("00000004-0000-0000-0000-000000000004");

        using FinanceDbContext context = CreateContext(database, TenantId);

        context.PnlLines.AddRange(
            Line(Guid.Parse("00000000-0000-0000-0000-000000000001"), firstOrderLine, "RUB", Day1),
            Line(Guid.Parse("00000000-0000-0000-0000-000000000002"), secondOrderLine, "RUB", Day2, PnlLineState.NotCalculated),
            Line(Guid.Parse("00000000-0000-0000-0000-000000000003"), thirdOrderLine, "RUB", Day3),
            Line(Guid.Parse("00000000-0000-0000-0000-000000000004"), otherCurrencyOrderLine, "USD", Day3));

        context.OrderLineRefs.AddRange(
            Reference(firstOrderLine, ChannelId),
            Reference(secondOrderLine, ChannelId),
            Reference(thirdOrderLine, OtherChannelId),
            Reference(otherCurrencyOrderLine, ChannelId));

        context.OrderLineCosts.Add(Cost(firstOrderLine, ProductId));

        await context.SaveChangesAsync();

        return new[] { firstOrderLine, secondOrderLine, thirdOrderLine };
    }

    private static PnlLine Line(
        Guid id,
        Guid orderLineId,
        string currency,
        DateOnly? saleDate,
        PnlLineState state = PnlLineState.Calculated,
        PnlLineCompleteness completeness = PnlLineCompleteness.Complete)
    {
        return new PnlLine
        {
            Id = id,
            OrderLineId = orderLineId,
            Currency = currency,
            SaleDate = saleDate,
            State = state,
            RevenueAmount = 100m,
            NetRevenueAmount = 90m,
            EntryCount = 1,
            CompletenessState = completeness,
            CreatedAt = DateTimeOffset.UnixEpoch,
        };
    }

    private static OrderLineRef Reference(Guid orderLineId, Guid channelId, Guid? channelProductId = null)
    {
        return new OrderLineRef
        {
            OrderLineId = orderLineId,
            OrderId = Guid.NewGuid(),
            ChannelId = channelId,
            ExternalOrderId = "order-" + orderLineId,
            ExternalLineId = "line-" + orderLineId,
            ExternalProductId = "product",
            ChannelProductId = channelProductId,
            Quantity = 1,
            OrderDate = DateTimeOffset.UnixEpoch,
            Currency = "RUB",
            UpdatedAt = DateTimeOffset.UnixEpoch,
        };
    }

    private static OrderLineRef ReferenceWith(
        Guid orderLineId,
        Guid channelId,
        string externalLineId,
        string externalOrderId,
        string externalProductId,
        DateTimeOffset orderDate)
    {
        return new OrderLineRef
        {
            OrderLineId = orderLineId,
            OrderId = Guid.NewGuid(),
            ChannelId = channelId,
            ExternalOrderId = externalOrderId,
            ExternalLineId = externalLineId,
            ExternalProductId = externalProductId,
            Quantity = 1,
            OrderDate = orderDate,
            Currency = "RUB",
            UpdatedAt = DateTimeOffset.UnixEpoch,
        };
    }

    private static PnlEntry Entry(
        Guid channelId,
        string? externalLineId,
        string? externalOrderId,
        string externalProductId,
        DateOnly documentDate)
    {
        return new PnlEntry
        {
            Id = Guid.NewGuid(),
            Source = externalLineId == null ? PnlSource.OzonAccrual : PnlSource.WbReport,
            ReportInstanceKey = "report",
            ExternalEntryId = Guid.NewGuid().ToString(),
            ChannelId = channelId,
            ExternalOrderId = externalOrderId,
            ExternalLineId = externalLineId,
            ExternalProductId = externalProductId,
            Kind = PnlEntryKind.Component,
            Type = PnlEntryType.Sale,
            Amount = 100m,
            Direction = PnlEntryDirection.Income,
            Currency = "RUB",
            DocumentDate = documentDate,
            PeriodFrom = documentDate,
            PeriodTo = documentDate,
            ImportedAt = DateTimeOffset.UnixEpoch,
            CreatedAt = DateTimeOffset.UnixEpoch,
        };
    }

    private static OrderLineCost Cost(Guid orderLineId, Guid productId)
    {
        return new OrderLineCost
        {
            OrderLineId = orderLineId,
            Status = OrderLineCostStatus.Resolved,
            ProductId = productId,
            AsOfDate = Day1,
            Quantity = 1,
            ResolvedAt = DateTimeOffset.UnixEpoch,
            CreatedAt = DateTimeOffset.UnixEpoch,
        };
    }

    private static OrderLineCost UnresolvedCost(Guid orderLineId)
    {
        return new OrderLineCost
        {
            OrderLineId = orderLineId,
            Status = OrderLineCostStatus.ProductNotLinked,
            ProductId = null,
            AsOfDate = Day1,
            Quantity = 1,
            IsComplete = false,
            ResolvedAt = DateTimeOffset.UnixEpoch,
            CreatedAt = DateTimeOffset.UnixEpoch,
        };
    }

    private static FinanceDbContext CreateContext(string database, Guid tenantId)
    {
        DbContextOptions<FinanceDbContext> options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase(database)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new FinanceDbContext(options, CreateTenantContext(tenantId));
    }

    private static ITenantContext CreateTenantContext(Guid tenantId)
    {
        Mock<ITenantContext> tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns(tenantId == Guid.Empty ? null : tenantId);

        return tenant.Object;
    }

    private static string NewDatabaseName()
    {
        return "pnl-read-" + Guid.NewGuid();
    }
}
