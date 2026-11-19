using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.Models;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Domain.Enums;
using Delobytes.App.Backend.Finance.Infrastructure.Persistence;
using Delobytes.App.Backend.Finance.Infrastructure.Persistence.Stores;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure.Finance;

/// <summary>
/// Guards the idempotency identity of the journal. The channel-side identities are unique per
/// seller account, so the channel has to be part of both the unique index and the lookup filter:
/// without it two shops of one marketplace collide on the same report and row numbers.
/// </summary>
public class PnlEntryIdentityTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ChannelId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void UniqueIndex_IncludesTheChannelAndTheInstance()
    {
        // Arrange
        using FinanceDbContext context = CreateContext();

        IEntityType entity = context.Model.FindEntityType(typeof(PnlEntry))!;

        // Act
        IIndex identityIndex = entity.GetIndexes().Single(index => index.IsUnique);

        // Assert
        identityIndex.Properties
            .Select(property => property.Name)
            .Should()
            .Equal(
                "TenantId",
                "ChannelId",
                "Source",
                "ReportInstanceKey",
                "ExternalEntryId",
                "ExternalProductId",
                "Kind",
                "Type");
    }

    [Fact]
    public void IdentityLookup_FiltersByChannelInSql()
    {
        // Arrange
        using FinanceDbContext context = CreateContext();
        PnlJournalStore store = new PnlJournalStore(context);

        // Act
        string sql = store
            .BuildIdentityCandidatesQuery(new[] { Key(ChannelId) })
            .ToQueryString();

        // Assert: the channel narrows the lookup in the database, not only in memory, so a shop
        // does not read the rows of its siblings.
        sql.Should().Contain("\"ChannelId\"");
        sql.Should().Contain("\"ReportInstanceKey\"");
        sql.Should().Contain("\"ExternalEntryId\"");
    }

    private static JournalIdentityKey Key(Guid channelId)
    {
        return new JournalIdentityKey(
            channelId,
            PnlSource.OzonAccrual,
            string.Empty,
            "accrual-1",
            "5022694666",
            PnlEntryKind.Component,
            PnlEntryType.Acquiring);
    }

    private static FinanceDbContext CreateContext()
    {
        DbContextOptions<FinanceDbContext> options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql("Host=localhost;Database=finance;Username=test;Password=test")
            .Options;

        Mock<ITenantContext> tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns(TenantId);

        return new FinanceDbContext(options, tenant.Object);
    }
}
