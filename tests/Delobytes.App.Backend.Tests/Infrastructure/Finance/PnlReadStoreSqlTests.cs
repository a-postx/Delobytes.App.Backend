using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Domain.Enums;
using Delobytes.App.Backend.Finance.Infrastructure.Persistence;
using Delobytes.App.Backend.Finance.Infrastructure.Persistence.Stores;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure.Finance;

/// <summary>
/// Guards the translation of the P&amp;L read queries into SQL. The joins, the filters and the
/// keyset must run in the database: a client-evaluated cursor would read the whole table, and the
/// old <c>"Id"::text</c> ordering could not use the primary key index.
/// </summary>
public class PnlReadStoreSqlTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Cursor = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void BuildLinesQuery_JoinsReferenceAndOptionalCostInSql()
    {
        using FinanceDbContext context = CreateContext();
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext());

        string sql = store
            .BuildLinesQuery("RUB", new PnlLineFilter())
            .OrderBy(row => row.Id)
            .Take(10)
            .ToQueryString();

        sql.Should().Contain("FROM finance.\"OrderLineRefs\"");
        sql.Should().Contain("FROM finance.\"OrderLineCosts\"");
        sql.Should().Contain("LEFT JOIN");
        sql.Should().Contain("ORDER BY");
        sql.Should().Contain("LIMIT");
    }

    [Fact]
    public void BuildLinesQuery_KeepsProductIdAndChannelProductIdApartInSql()
    {
        using FinanceDbContext context = CreateContext();
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext());

        string sql = store
            .BuildLinesQuery("RUB", new PnlLineFilter())
            .OrderBy(row => row.Id)
            .Take(10)
            .ToQueryString();

        // The product comes from the resolved cost fact and the channel product is a separate
        // column: a CASE fallback would mix two identifier domains in one field. The projection
        // carries exactly one conditional — the margin ordering key — and it is built from the
        // revenue and the cost, never from an identifier.
        sql.Should().Contain("\"ProductId\"");
        sql.Should().Contain("\"ChannelProductId\"");

        string[] conditionals = sql.Split("CASE").Skip(1).ToArray();

        conditionals.Should().ContainSingle();
        conditionals[0].Should().Contain("\"NetRevenueAmount\"");
    }

    [Fact]
    public void BuildLinesQuery_SelectsTheQuantityFromTheJoinedReference()
    {
        using FinanceDbContext context = CreateContext();
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext());

        string sql = store
            .BuildLinesQuery("RUB", new PnlLineFilter())
            .OrderBy(row => row.Id)
            .Take(10)
            .ToQueryString();

        // The ordered units come from the reference the query already joins: a second round trip or
        // a fallback to the cost fact's quantity would be a second source of the same fact.
        string selected = sql.Split("WHERE")[0];

        selected.Should().Contain("FROM finance.\"OrderLineRefs\"");
        selected.Should().Contain("\"Quantity\"");
    }

    [Fact]
    public void BuildPageQuery_WithDateCursor_ComparesUuidWithoutTextCast()
    {
        using FinanceDbContext context = CreateContext();
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext());

        PnlLineSort sort = new PnlLineSort { Key = PnlLineSortKey.Date, Descending = true };
        PnlLineCursor cursor = PnlLineCursor.Parse($"date|desc|2026-03-01|{Cursor}", sort)!;

        string sql = store
            .BuildPageQuery("RUB", new PnlLineFilter(), sort, cursor)
            .Take(10)
            .ToQueryString();

        sql.Should().Contain("\"Id\" > @");
        sql.Should().NotContain("::text");
    }

    [Fact]
    public void BuildPageQuery_OrdersTheDateKeyThenTheIdentifierInSql()
    {
        using FinanceDbContext context = CreateContext();
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext());

        string sql = store
            .BuildPageQuery("RUB", new PnlLineFilter(), PnlLineSort.Default, null)
            .Take(10)
            .ToQueryString();

        // The period date is a COALESCE over the two date columns, and the identifier is the
        // tiebreak that keeps the keyset total when two lines share a date.
        int ordering = sql.IndexOf("ORDER BY", StringComparison.Ordinal);

        ordering.Should().BeGreaterThan(0);

        string orderBy = sql.Substring(ordering);

        orderBy.Should().Contain("COALESCE(");
        orderBy.Should().Contain("DESC");
        orderBy.Should().Contain("\"Id\"");
    }

    [Fact]
    public void BuildPageQuery_OrdersTheMarginKeyInSql()
    {
        using FinanceDbContext context = CreateContext();
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext());

        PnlLineSort sort = new PnlLineSort { Key = PnlLineSortKey.Margin, Descending = true };

        string sql = store
            .BuildPageQuery("RUB", new PnlLineFilter(), sort, null)
            .Take(10)
            .ToQueryString();

        string orderBy = sql.Substring(sql.IndexOf("ORDER BY", StringComparison.Ordinal));

        orderBy.Should().Contain("CASE");
        orderBy.Should().Contain("\"NetRevenueAmount\"");
        orderBy.Should().Contain("\"ProductionCostAmount\"");
        orderBy.Should().Contain("\"Id\"");
    }

    [Fact]
    public void BuildPageQuery_WithMarginCursor_AppliesTheKeysetInSql()
    {
        using FinanceDbContext context = CreateContext();
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext());

        PnlLineSort sort = new PnlLineSort { Key = PnlLineSortKey.Margin, Descending = true };
        PnlLineCursor cursor = PnlLineCursor.Parse($"margin|desc|1234.50|{Cursor}", sort)!;

        string sql = store
            .BuildPageQuery("RUB", new PnlLineFilter(), sort, cursor)
            .Take(10)
            .ToQueryString();

        // The unknown margin is its own block of the order, so the predicate must select it as well
        // as the values below the cursor.
        sql.Should().Contain("IS NULL");
        sql.Should().Contain("@margin");
    }

    [Fact]
    public void BuildPageQuery_WithUnknownMarginCursor_StaysInsideTheUnknownBlockInSql()
    {
        using FinanceDbContext context = CreateContext();
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext());

        PnlLineSort sort = new PnlLineSort { Key = PnlLineSortKey.Margin, Descending = true };
        PnlLineCursor cursor = PnlLineCursor.Parse($"margin|desc||{Cursor}", sort)!;

        string sql = store
            .BuildPageQuery("RUB", new PnlLineFilter(), sort, cursor)
            .Take(10)
            .ToQueryString();

        sql.Should().Contain("IS NULL");
        sql.Should().Contain("\"Id\" > @");
    }

    [Fact]
    public void BuildLinesQuery_WithoutCursor_HasNoKeysetPredicate()
    {
        using FinanceDbContext context = CreateContext();
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext());

        string sql = store
            .BuildLinesQuery("RUB", new PnlLineFilter())
            .OrderBy(row => row.Id)
            .Take(10)
            .ToQueryString();

        sql.Should().NotContain("\"Id\" > @");
    }

    [Fact]
    public void BuildLinesQuery_TranslatesEveryFilterToSql()
    {
        using FinanceDbContext context = CreateContext();
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext());

        PnlLineFilter filter = new()
        {
            ChannelId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ProductId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            ChannelProductId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            From = new DateOnly(2026, 3, 1),
            To = new DateOnly(2026, 3, 31),
            State = PnlLineState.Calculated,
            CompletenessStates = new[]
            {
                PnlLineCompleteness.Complete,
                PnlLineCompleteness.ClosedWithException,
            },
        };

        string sql = store
            .BuildLinesQuery("RUB", filter)
            .OrderBy(row => row.Id)
            .Take(10)
            .ToQueryString();

        sql.Should().Contain("\"ChannelId\" = @");
        sql.Should().Contain("\"ProductId\" = @");
        sql.Should().Contain("\"ChannelProductId\" = @");
        // The period date is the sale date when the line sold and the financial period otherwise, so
        // the predicate is a COALESCE over both columns rather than a comparison on one of them.
        sql.Should().Contain("COALESCE(");
        sql.Should().Contain("\"SaleDate\"");
        sql.Should().Contain("\"FinancialPeriodFrom\"");
        sql.Should().Contain(">= @");
        sql.Should().Contain("<= @");
        sql.Should().Contain("\"State\" = @");
        sql.Should().Contain("\"CompletenessState\"");
        sql.Should().Contain("\"Currency\" = @");
    }

    [Fact]
    public void BuildFilteredQuery_ExcludedCountRunsInSql()
    {
        using FinanceDbContext context = CreateContext();
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext());
        string currency = "RUB";

        string sql = store
            .BuildFilteredQuery(new PnlLineFilter { From = new DateOnly(2026, 3, 1) })
            .Where(row => row.Currency != currency)
            .ToQueryString();

        sql.Should().Contain("\"Currency\" <> @");
        sql.Should().Contain("COALESCE(");
        sql.Should().Contain("\"FinancialPeriodFrom\"");
        sql.Should().Contain(">= @");
        sql.Should().Contain("FROM finance.\"OrderLineRefs\"");
    }

    [Fact]
    public void BuildAttributionCandidatesQuery_NarrowsByIdentitySetsInSql()
    {
        using FinanceDbContext context = CreateContext();
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext());

        string sql = store
            .BuildAttributionCandidatesQuery(new[] { Entry("srid-1", null) })
            .ToQueryString();

        sql.Should().Contain("FROM finance.\"OrderLineRefs\"");
        sql.Should().Contain("\"ChannelId\" = ANY");
        sql.Should().Contain("\"ExternalLineId\" = ANY");
        sql.Should().Contain("\"TenantId\" = @ef_filter__TenantId");
        sql.Should().NotContain("::text");
    }

    [Fact]
    public void BuildAttributionCandidatesQuery_WithOnlyOrderKeys_FiltersByOrderId()
    {
        using FinanceDbContext context = CreateContext();
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext());

        string sql = store
            .BuildAttributionCandidatesQuery(new[] { Entry(null, "order-1") })
            .ToQueryString();

        sql.Should().Contain("\"ExternalOrderId\" = ANY");
        sql.Should().NotContain("\"ExternalLineId\" = ANY");
    }

    [Fact]
    public void BuildAttributionCandidatesQuery_WithoutEntries_ProducesAnEmptyQuery()
    {
        using FinanceDbContext context = CreateContext();
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext());

        string sql = store.BuildAttributionCandidatesQuery(Array.Empty<PnlEntry>()).ToQueryString();

        sql.Should().Contain("FROM finance.\"OrderLineRefs\"");
        sql.Should().Contain("WHERE");
    }

    [Fact]
    public void BuildLinesQuery_MultipleProductIds_FiltersInSql()
    {
        // The list must become one ANY(...) predicate: materialising the ids in memory would read
        // the whole tenant.
        using FinanceDbContext context = CreateContext();
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext());

        string sql = store
            .BuildLinesQuery(
                "RUB",
                new PnlLineFilter { ProductIds = new[] { Guid.NewGuid(), Guid.NewGuid() } })
            .ToQueryString();

        sql.Should().Contain("= ANY");
        sql.Should().Contain("\"ProductId\"");
    }

    private static PnlEntry Entry(string? externalLineId, string? externalOrderId)
    {
        return new PnlEntry
        {
            Id = Guid.NewGuid(),
            Source = externalLineId == null ? PnlSource.OzonAccrual : PnlSource.WbReport,
            ReportInstanceKey = "report",
            ExternalEntryId = Guid.NewGuid().ToString(),
            ChannelId = TenantId,
            ExternalOrderId = externalOrderId,
            ExternalLineId = externalLineId,
            ExternalProductId = "sku-1",
            Kind = PnlEntryKind.Component,
            Type = PnlEntryType.Sale,
            Amount = 100m,
            Direction = PnlEntryDirection.Income,
            Currency = "RUB",
            DocumentDate = new DateOnly(2026, 3, 1),
            PeriodFrom = new DateOnly(2026, 3, 1),
            PeriodTo = new DateOnly(2026, 3, 1),
            ImportedAt = DateTimeOffset.UnixEpoch,
            CreatedAt = DateTimeOffset.UnixEpoch,
        };
    }

    private static FinanceDbContext CreateContext()
    {
        DbContextOptions<FinanceDbContext> options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql("Host=localhost;Database=finance;Username=test;Password=test")
            .Options;

        return new FinanceDbContext(options, CreateTenantContext());
    }

    private static ITenantContext CreateTenantContext()
    {
        Mock<ITenantContext> tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns(TenantId);

        return tenant.Object;
    }
}
