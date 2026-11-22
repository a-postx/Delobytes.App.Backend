using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.Ingestion;
using Delobytes.App.Backend.Finance.Application.Journal;
using Delobytes.App.Backend.Finance.Application.Models;
using Delobytes.App.Backend.Finance.Application.Projection;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Domain.Enums;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Tests.Application.Finance.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Finance;

/// <summary>
/// Tests for the finance ingestion path: the contract-to-domain mapping must be exhaustive, the
/// counters must describe what actually happened, and a redelivered batch must create nothing new.
/// </summary>
public class FinanceEntriesBatchImporterTests
{
    private static readonly Guid ChannelId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Mapper_EveryContractEnumValue_HasAnExplicitMapping()
    {
        foreach (FinanceEntrySource value in Enum.GetValues<FinanceEntrySource>())
        {
            FinanceEntryMapper.MapSource(value).ToString().Should().Be(value.ToString());
        }

        foreach (FinanceEntryKind value in Enum.GetValues<FinanceEntryKind>())
        {
            FinanceEntryMapper.MapKind(value).ToString().Should().Be(value.ToString());
        }

        foreach (FinanceEntryType value in Enum.GetValues<FinanceEntryType>())
        {
            FinanceEntryMapper.MapType(value).ToString().Should().Be(value.ToString());
        }

        foreach (FinanceEntryDirection value in Enum.GetValues<FinanceEntryDirection>())
        {
            FinanceEntryMapper.MapDirection(value).ToString().Should().Be(value.ToString());
        }
    }

    [Fact]
    public void Mapper_UnmappedValue_Throws()
    {
        Action act = () => FinanceEntryMapper.MapType((FinanceEntryType)999);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ProcessAsync_DuplicateAndNewEntry_ReportsBothCounters()
    {
        // Arrange
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        RecordingEventPublisher publisher = new RecordingEventPublisher();
        FinanceEntriesBatchImporter importer = CreateImporter(journal, publisher);

        journal.Entries.Add(new PnlEntry
        {
            Id = Guid.NewGuid(),
            Source = PnlSource.WbReport,
            ReportInstanceKey = "799692300:2026-08-03",
            ExternalEntryId = "rrd-1",
            ChannelId = ChannelId,
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
        });

        FinanceEntriesBatchImportRequestedEvent message = new FinanceEntriesBatchImportRequestedEvent
        {
            JobRunId = Guid.NewGuid(),
            ChannelId = ChannelId,
            BatchId = Guid.NewGuid(),
            Entries = new List<ChannelFinanceEntrySnapshot>
            {
                Snapshot("799692300:2026-08-03", "rrd-1"),
                Snapshot("799692300:2026-08-03", "rrd-2"),
            },
        };

        // Act
        await importer.ProcessAsync(message, CancellationToken.None);

        // Assert
        FinanceEntriesBatchImportCompletedEvent completed = publisher.Events
            .OfType<FinanceEntriesBatchImportCompletedEvent>()
            .Should().ContainSingle().Subject;

        completed.Appended.Should().Be(1);
        completed.Duplicates.Should().Be(1);
        completed.Failed.Should().Be(0);
        journal.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task ProcessAsync_PartialMappingFailure_StillPublishesTheCompletedEvent()
    {
        // Arrange
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        RecordingEventPublisher publisher = new RecordingEventPublisher();
        FinanceEntriesBatchImporter importer = CreateImporter(journal, publisher);

        ChannelFinanceEntrySnapshot invalid = Snapshot("799692300:2026-08-03", "rrd-1");
        invalid = invalid with { Type = (FinanceEntryType)999 };

        FinanceEntriesBatchImportRequestedEvent message = new FinanceEntriesBatchImportRequestedEvent
        {
            JobRunId = Guid.NewGuid(),
            ChannelId = ChannelId,
            BatchId = Guid.NewGuid(),
            Entries = new List<ChannelFinanceEntrySnapshot>
            {
                invalid,
                Snapshot("799692300:2026-08-03", "rrd-2"),
            },
        };

        // Act
        await importer.ProcessAsync(message, CancellationToken.None);

        // Assert
        FinanceEntriesBatchImportCompletedEvent completed = publisher.Events
            .OfType<FinanceEntriesBatchImportCompletedEvent>()
            .Should().ContainSingle().Subject;

        completed.Failed.Should().Be(1);
        completed.Appended.Should().Be(1);
    }

    [Fact]
    public async Task ProcessAsync_RedeliveredBatch_CreatesNoNewRows()
    {
        // Arrange
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        RecordingEventPublisher publisher = new RecordingEventPublisher();
        FinanceEntriesBatchImporter importer = CreateImporter(journal, publisher);

        FinanceEntriesBatchImportRequestedEvent message = new FinanceEntriesBatchImportRequestedEvent
        {
            JobRunId = Guid.NewGuid(),
            ChannelId = ChannelId,
            BatchId = Guid.NewGuid(),
            Entries = new List<ChannelFinanceEntrySnapshot> { Snapshot("799692300:2026-08-03", "rrd-1") },
        };

        // Act
        await importer.ProcessAsync(message, CancellationToken.None);
        int afterFirst = journal.Entries.Count;

        await importer.ProcessAsync(message, CancellationToken.None);

        // Assert
        afterFirst.Should().Be(1);
        journal.Entries.Should().HaveCount(1);

        publisher.Events.OfType<FinanceEntriesBatchImportCompletedEvent>()
            .Select(e => e.Appended)
            .Should().Equal(1, 0);
    }

    [Fact]
    public async Task ProcessAsync_RecomputeFailsForOneLine_StillPublishesWithRecomputeFailedCounter()
    {
        // Arrange: the journal already carries a corrupted entry for the line (a payout typed as a
        // component), so PnlLineAggregator.Calculate throws once this line is recomputed. The
        // import of the new, valid entry must still succeed and still publish its completed event.
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        InMemoryOrderLineRefStore refs = new InMemoryOrderLineRefStore();
        RecordingEventPublisher publisher = new RecordingEventPublisher();
        FinanceEntriesBatchImporter importer = CreateImporter(journal, publisher, refs);

        Guid orderLineId = Guid.NewGuid();
        refs.Refs[orderLineId] = new OrderLineRef
        {
            OrderLineId = orderLineId,
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

        journal.Entries.Add(new PnlEntry
        {
            Id = Guid.NewGuid(),
            Source = PnlSource.WbReport,
            ReportInstanceKey = "corrupt:1",
            ExternalEntryId = "rrd-corrupt",
            ChannelId = ChannelId,
            ExternalLineId = "srid-1",
            ExternalProductId = "174371959",
            Kind = PnlEntryKind.Component,
            Type = PnlEntryType.Payout,
            Amount = 50m,
            Direction = PnlEntryDirection.Income,
            Currency = "RUB",
            DocumentDate = new DateOnly(2026, 8, 1),
            PeriodFrom = new DateOnly(2026, 7, 27),
            PeriodTo = new DateOnly(2026, 8, 2),
        });

        FinanceEntriesBatchImportRequestedEvent message = new FinanceEntriesBatchImportRequestedEvent
        {
            JobRunId = Guid.NewGuid(),
            ChannelId = ChannelId,
            BatchId = Guid.NewGuid(),
            Entries = new List<ChannelFinanceEntrySnapshot> { Snapshot("799692300:2026-08-03", "rrd-1") },
        };

        // Act
        await importer.ProcessAsync(message, CancellationToken.None);

        // Assert
        FinanceEntriesBatchImportCompletedEvent completed = publisher.Events
            .OfType<FinanceEntriesBatchImportCompletedEvent>()
            .Should().ContainSingle().Subject;

        completed.Appended.Should().Be(1);
        completed.RecomputeFailed.Should().Be(1);
    }

    [Fact]
    public async Task ProcessAsync_NewEntry_RecordsCheckpointOfTheDelivery()
    {
        // Arrange
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        RecordingEventPublisher publisher = new RecordingEventPublisher();
        InMemoryPnlFeedStateStore feedStore = new InMemoryPnlFeedStateStore();
        FinanceEntriesBatchImporter importer = CreateImporter(journal, publisher, feedStore: feedStore);

        Guid jobRunId = Guid.NewGuid();
        Guid connectionId = Guid.NewGuid();

        FinanceEntriesBatchImportRequestedEvent message = new FinanceEntriesBatchImportRequestedEvent
        {
            JobRunId = jobRunId,
            ConnectionId = connectionId,
            ChannelId = ChannelId,
            BatchId = Guid.NewGuid(),
            Entries = new List<ChannelFinanceEntrySnapshot>
            {
                Snapshot("799692300:2026-08-03", "rrd-1"),
            },
        };

        // Act
        await importer.ProcessAsync(message, CancellationToken.None);

        // Assert
        PnlFeedCheckpoint checkpoint = feedStore.Recorded.Should().ContainSingle().Subject;
        checkpoint.ChannelId.Should().Be(ChannelId);
        checkpoint.JobRunId.Should().Be(jobRunId);
        checkpoint.BatchCompletedAt.Should().Be(Now);
        checkpoint.Appended.Should().Be(1);
        checkpoint.Duplicates.Should().Be(0);
        checkpoint.Failed.Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_DuplicateOnlyBatch_StillRecordsTheDelivery()
    {
        // Arrange
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        RecordingEventPublisher publisher = new RecordingEventPublisher();
        InMemoryPnlFeedStateStore feedStore = new InMemoryPnlFeedStateStore();
        FinanceEntriesBatchImporter importer = CreateImporter(journal, publisher, feedStore: feedStore);

        journal.Entries.Add(new PnlEntry
        {
            Id = Guid.NewGuid(),
            Source = PnlSource.WbReport,
            ReportInstanceKey = "799692300:2026-08-03",
            ExternalEntryId = "rrd-1",
            ChannelId = ChannelId,
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
        });

        FinanceEntriesBatchImportRequestedEvent message = new FinanceEntriesBatchImportRequestedEvent
        {
            JobRunId = Guid.NewGuid(),
            ChannelId = ChannelId,
            BatchId = Guid.NewGuid(),
            Entries = new List<ChannelFinanceEntrySnapshot>
            {
                Snapshot("799692300:2026-08-03", "rrd-1"),
            },
        };

        // Act
        await importer.ProcessAsync(message, CancellationToken.None);

        // Assert
        // A report that delivered nothing new must still refresh the checkpoint: otherwise the age
        // of the data on the dashboard would keep growing after a successful re-sync.
        PnlFeedCheckpoint checkpoint = feedStore.Recorded.Should().ContainSingle().Subject;
        checkpoint.Appended.Should().Be(0);
        checkpoint.Duplicates.Should().Be(1);
    }

    [Fact]
    public async Task ProcessAsync_MappingFailure_CountsIntoTheCheckpoint()
    {
        // Arrange
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        RecordingEventPublisher publisher = new RecordingEventPublisher();
        InMemoryPnlFeedStateStore feedStore = new InMemoryPnlFeedStateStore();
        FinanceEntriesBatchImporter importer = CreateImporter(journal, publisher, feedStore: feedStore);

        ChannelFinanceEntrySnapshot invalid = Snapshot("799692300:2026-08-03", "rrd-1");
        invalid = invalid with { Type = (FinanceEntryType)999 };

        FinanceEntriesBatchImportRequestedEvent message = new FinanceEntriesBatchImportRequestedEvent
        {
            JobRunId = Guid.NewGuid(),
            ChannelId = ChannelId,
            BatchId = Guid.NewGuid(),
            Entries = new List<ChannelFinanceEntrySnapshot>
            {
                invalid,
                Snapshot("799692300:2026-08-03", "rrd-2"),
            },
        };

        // Act
        await importer.ProcessAsync(message, CancellationToken.None);

        // Assert
        PnlFeedCheckpoint checkpoint = feedStore.Recorded.Should().ContainSingle().Subject;
        checkpoint.Appended.Should().Be(1);
        checkpoint.Failed.Should().Be(1);
    }

    private static FinanceEntriesBatchImporter CreateImporter(
        InMemoryPnlJournalStore journal,
        RecordingEventPublisher publisher,
        InMemoryOrderLineRefStore? refs = null,
        InMemoryPnlFeedStateStore? feedStore = null)
    {
        FixedTimeProvider timeProvider = new FixedTimeProvider(Now);
        refs ??= new InMemoryOrderLineRefStore();

        JournalAppender appender = new JournalAppender(
            journal,
            refs,
            timeProvider,
            NullLogger<JournalAppender>.Instance);

        PnlLineProjector projector = new PnlLineProjector(
            journal,
            refs,
            new InMemoryOrderLineCostStore(),
            new InMemoryPnlExpectationStore(),
            new InMemoryPnlLineStore(),
            timeProvider,
            NullLogger<PnlLineProjector>.Instance);

        return new FinanceEntriesBatchImporter(
            appender,
            projector,
            feedStore ?? new InMemoryPnlFeedStateStore(),
            publisher,
            new StubTenantContext(),
            timeProvider,
            NullLogger<FinanceEntriesBatchImporter>.Instance);
    }

    private static ChannelFinanceEntrySnapshot Snapshot(string reportInstanceKey, string externalEntryId)
    {
        return new ChannelFinanceEntrySnapshot
        {
            Source = FinanceEntrySource.WbReport,
            ReportInstanceKey = reportInstanceKey,
            ExternalEntryId = externalEntryId,
            ExternalLineId = "srid-1",
            ExternalProductId = "174371959",
            Kind = FinanceEntryKind.Component,
            Type = FinanceEntryType.Sale,
            Amount = 204m,
            Direction = FinanceEntryDirection.Income,
            Currency = "RUB",
            DocumentDate = new DateOnly(2026, 8, 1),
            PeriodFrom = new DateOnly(2026, 7, 27),
            PeriodTo = new DateOnly(2026, 8, 2),
        };
    }
}
