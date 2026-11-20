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
            .BuildLinesQuery("RUB", new PnlLineFilter(), null)
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
            .BuildLinesQuery("RUB", new PnlLineFilter(), null)
            .OrderBy(row => row.Id)
            .Take(10)
            .ToQueryString();

        // The product comes from the resolved cost fact and the channel product is a separate
        // column: a CASE fallback would mix two identifier domains in one field.
        sql.Should().Contain("\"ProductId\"");
        sql.Should().Contain("\"ChannelProductId\"");
        sql.Should().NotContain("CASE");
    }

    [Fact]
    public void BuildLinesQuery_WithCursor_ComparesUuidWithoutTextCast()
    {
        using FinanceDbContext context = CreateContext();
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext());

        string sql = store
            .BuildLinesQuery("RUB", new PnlLineFilter(), Cursor)
            .OrderBy(row => row.Id)
            .Take(10)
            .ToQueryString();

        sql.Should().Contain("\"Id\" > @");
        sql.Should().NotContain("::text");
    }

    [Fact]
    public void BuildLinesQuery_WithoutCursor_HasNoKeysetPredicate()
    {
        using FinanceDbContext context = CreateContext();
        PnlReadStore store = new PnlReadStore(context, CreateTenantContext());

        string sql = store
            .BuildLinesQuery("RUB", new PnlLineFilter(), null)
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
            .BuildLinesQuery("RUB", filter, null)
            .OrderBy(row => row.Id)
            .Take(10)
            .ToQueryString();

        sql.Should().Contain("\"ChannelId\" = @");
        sql.Should().Contain("\"ProductId\" = @");
        sql.Should().Contain("\"ChannelProductId\" = @");
        sql.Should().Contain("\"SaleDate\" >= @");
        sql.Should().Contain("\"SaleDate\" <= @");
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
        sql.Should().Contain("\"SaleDate\" >= @");
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
