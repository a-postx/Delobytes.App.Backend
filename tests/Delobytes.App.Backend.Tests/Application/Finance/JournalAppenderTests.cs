using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Finance.Application.Journal;
using Delobytes.App.Backend.Finance.Application.Models;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Domain.Enums;
using Delobytes.App.Backend.Tests.Application.Finance.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Finance;

/// <summary>
/// Tests for JournalAppender. Every skipped row is money that disappears from the result, so the
/// counters are the contract: a duplicate is not an error, a rejected row is, and a new report
/// instance whose rows match nothing is the signal that row-level supersede stopped working.
/// </summary>
public class JournalAppenderTests
{
    private static readonly Guid ChannelId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherChannelId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AppendAsync_ExistingIdempotencyKey_IsSkippedAsDuplicate()
    {
        // Arrange
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        journal.Entries.Add(Entry("799692300:2026-08-03", "rrd-1"));

        JournalAppender appender = CreateAppender(journal, new InMemoryOrderLineRefStore());

        // Act
        JournalAppendResult result = await appender.AppendAsync(
            new[] { Entry("799692300:2026-08-03", "rrd-1") },
            CancellationToken.None);

        // Assert
        result.Appended.Should().Be(0);
        result.Duplicates.Should().Be(1);
        journal.Entries.Should().HaveCount(1);
    }

    [Fact]
    public async Task AppendAsync_SameRowIdentityOnAnotherChannel_IsAppendedNotDeduplicated()
    {
        // Arrange: two shops of one marketplace number their reports and rows independently, so the
        // same report instance and row identity on another channel is a different fact.
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        journal.Entries.Add(Entry("799692300:2026-08-03", "rrd-1"));

        JournalAppender appender = CreateAppender(journal, new InMemoryOrderLineRefStore());

        PnlEntry otherShop = Entry("799692300:2026-08-03", "rrd-1");
        otherShop.ChannelId = OtherChannelId;

        // Act
        JournalAppendResult result = await appender.AppendAsync(new[] { otherShop }, CancellationToken.None);

        // Assert
        result.Appended.Should().Be(1);
        result.Duplicates.Should().Be(0);
        journal.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task AppendAsync_NegativeAmountOrNoIdentity_IsRejected()
    {
        // Arrange
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        JournalAppender appender = CreateAppender(journal, new InMemoryOrderLineRefStore());

        PnlEntry negative = Entry("799692300:2026-08-03", "rrd-1");
        negative.Amount = -1m;

        PnlEntry noIdentity = Entry("799692300:2026-08-03", "rrd-2");
        noIdentity.ExternalLineId = null;
        noIdentity.ExternalOrderId = null;

        // Act
        JournalAppendResult result = await appender.AppendAsync(
            new[] { negative, noIdentity },
            CancellationToken.None);

        // Assert
        result.Appended.Should().Be(0);
        result.Rejected.Should().Be(2);
        journal.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task AppendAsync_NewReportInstance_SupersedesTheOlderEntry()
    {
        // Arrange: the report was re-issued and repeated the same row with a corrected amount.
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();

        PnlEntry oldEntry = Entry("799692300:2026-08-03", "rrd-1");
        oldEntry.Amount = 100m;
        journal.Entries.Add(oldEntry);

        JournalAppender appender = CreateAppender(journal, new InMemoryOrderLineRefStore());

        PnlEntry corrected = Entry("799692300:2026-08-10", "rrd-1");
        corrected.Amount = 90m;

        // Act
        JournalAppendResult result = await appender.AppendAsync(new[] { corrected }, CancellationToken.None);

        // Assert
        result.Appended.Should().Be(1);
        result.Superseded.Should().Be(1);
        result.UnmatchedInstanceRows.Should().Be(0);

        oldEntry.SupersededByEntryId.Should().Be(corrected.Id);
        oldEntry.SupersededAt.Should().Be(Now);
    }

    [Fact]
    public async Task AppendAsync_NewInstanceWhoseRowsMatchNothing_CountsUnmatchedInstanceRows()
    {
        // Arrange: the re-issued report changed the row identity, so row-level supersede cannot work.
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        journal.Entries.Add(Entry("799692300:2026-08-03", "rrd-1"));

        JournalAppender appender = CreateAppender(journal, new InMemoryOrderLineRefStore());

        PnlEntry changedIdentity = Entry("799692300:2026-08-10", "rrd-99");

        // Act
        JournalAppendResult result = await appender.AppendAsync(
            new[] { changedIdentity },
            CancellationToken.None);

        // Assert
        result.Appended.Should().Be(1);
        result.Superseded.Should().Be(0);
        result.UnmatchedInstanceRows.Should().Be(1);
    }

    [Fact]
    public async Task AppendAsync_FirstInstanceOfAReport_IsNotCountedAsUnmatched()
    {
        // Arrange: no predecessor exists, so a row that matches nothing is normal.
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        JournalAppender appender = CreateAppender(journal, new InMemoryOrderLineRefStore());

        // Act
        JournalAppendResult result = await appender.AppendAsync(
            new[] { Entry("799692300:2026-08-03", "rrd-1") },
            CancellationToken.None);

        // Assert
        result.UnmatchedInstanceRows.Should().Be(0);
    }

    [Fact]
    public async Task AppendAsync_EntryWithoutRef_IsCountedAsUnattributed()
    {
        // Arrange: the order line has not arrived yet.
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        JournalAppender appender = CreateAppender(journal, new InMemoryOrderLineRefStore());

        // Act
        JournalAppendResult result = await appender.AppendAsync(
            new[] { Entry("799692300:2026-08-03", "rrd-1") },
            CancellationToken.None);

        // Assert
        result.Appended.Should().Be(1);
        result.Unattributed.Should().Be(1);
        result.AffectedOrderLineIds.Should().BeEmpty();
    }

    [Fact]
    public async Task AppendAsync_EntryMatchingARef_ReturnsTheAffectedLine()
    {
        // Arrange
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        InMemoryOrderLineRefStore refs = new InMemoryOrderLineRefStore();

        OrderLineRef line = new OrderLineRef
        {
            OrderLineId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            ChannelId = ChannelId,
            ExternalOrderId = "order-1",
            ExternalLineId = "srid-1",
            ExternalProductId = "174371959",
            Quantity = 1,
            OrderDate = Now,
            Currency = "RUB",
            UpdatedAt = Now,
        };

        refs.Refs[line.OrderLineId] = line;

        JournalAppender appender = CreateAppender(journal, refs);

        // Act
        JournalAppendResult result = await appender.AppendAsync(
            new[] { Entry("799692300:2026-08-03", "rrd-1") },
            CancellationToken.None);

        // Assert
        result.Unattributed.Should().Be(0);
        result.AffectedOrderLineIds.Should().ContainSingle().Which.Should().Be(line.OrderLineId);
    }

    private static JournalAppender CreateAppender(
        InMemoryPnlJournalStore journal,
        InMemoryOrderLineRefStore refs)
    {
        return new JournalAppender(
            journal,
            refs,
            new FixedTimeProvider(Now),
            NullLogger<JournalAppender>.Instance);
    }

    private static PnlEntry Entry(string reportInstanceKey, string externalEntryId)
    {
        return new PnlEntry
        {
            Source = PnlSource.WbReport,
            ReportInstanceKey = reportInstanceKey,
            ExternalEntryId = externalEntryId,
            ChannelId = ChannelId,
            ExternalOrderId = null,
            ExternalLineId = "srid-1",
            ExternalProductId = "174371959",
            Kind = PnlEntryKind.Component,
            Type = PnlEntryType.Sale,
            Amount = 204m,
            Direction = PnlEntryDirection.Income,
            Currency = "RUB",
            DocumentDate = new DateOnly(2026, 8, 1),
            PeriodFrom = new DateOnly(2026, 7, 27),
            PeriodTo = new DateOnly(2026, 8, 2),
        };
    }
}
