using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Finance;
using Delobytes.App.Backend.Integrations.Application.Jobs;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Finance;

/// <summary>
/// Tests the page loop of the finance import: progress must survive a channel pause, a window must
/// be read exactly once, and it must be possible to converge on a document that takes several
/// requests when the channel allows one request per minute.
/// </summary>
public class FinanceImportOrchestratorTests
{
    private static readonly DateOnly DocumentDate = new(2026, 11, 1);

    [Fact]
    public async Task ProcessAsync_ThrottledBetweenPages_PublishesWhatWasReadAndResumesFromTheSavedCursor()
    {
        Harness harness = new Harness("wildberries", JobType.WbFinanceReportImport);

        harness.Source.RespondWith(_ => Page(1500, "p1", nextCursor: "page-2", isComplete: false));
        harness.Source.RespondWith(_ => Throttled(harness.Clock.GetUtcNow().AddMinutes(1)));

        await harness.Orchestrator.ProcessAsync(
            harness.Run.Id, harness.Connection.Id, CancellationToken.None);

        harness.Source.Requests[0].Cursor.Should().BeNull();
        harness.Publisher.FinanceBatches.Should().HaveCount(2, "1500 entries are published as 1000 + 500");
        harness.Run.Status.Should().Be(JobStatus.Running);
        harness.Run.StartedAt.Should().NotBeNull();
        harness.Run.NotBefore.Should().Be(harness.Clock.GetUtcNow().AddMinutes(1));
        harness.Scope.Entered.Should().Be(1, "the channel calls must be attributable to the run");

        FinanceImportCheckpointPayload state = await harness.ReadStateAsync();
        state.Cursor.Should().Be("page-2");
        state.PublishedBatches.Should().Be(2);
        state.WindowCompleted.Should().BeFalse();

        harness.Clock.Advance(TimeSpan.FromMinutes(2));
        harness.Source.RespondWith(_ => Page(200, "p2", nextCursor: null, isComplete: true));

        await harness.Orchestrator.ProcessAsync(
            harness.Run.Id, harness.Connection.Id, CancellationToken.None);

        harness.Source.Requests[2].Cursor.Should().Be(
            "page-2", "the retry must continue the document, not restart it");
        harness.Source.Requests.Should().HaveCount(3, "the first page must not be read twice");
        harness.Publisher.FinanceBatches.Should().HaveCount(3);
        harness.Run.ExpectedBatchCount.Should().Be(3);
        harness.Run.NotBefore.Should().BeNull("the deferral is spent once the run is dispatched again");

        FinanceImportCheckpointPayload completed = await harness.ReadStateAsync();
        completed.WindowCompleted.Should().BeTrue();
        completed.Cursor.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAsync_WhenEveryBatchResultIsAlreadyRecorded_FinalisesOnceAndMovesTheWatermark()
    {
        Harness harness = new Harness("wildberries", JobType.WbFinanceReportImport);

        harness.Source.RespondWith(_ => Page(1500, "p1", nextCursor: null, isComplete: true));

        await harness.Orchestrator.ProcessAsync(
            harness.Run.Id, harness.Connection.Id, CancellationToken.None);

        harness.Publisher.FinanceBatches.Should().HaveCount(2);
        harness.Run.ExpectedBatchCount.Should().Be(2);
        harness.Watermarks.Find(harness.Connection.Id, JobType.WbFinanceReportImport)
            .Should().BeNull("the watermark moves only once the batches are confirmed");

        foreach (FinanceEntriesBatchImportRequestedEvent batch in harness.Publisher.FinanceBatches)
        {
            harness.JobRuns.RecordBatchResult(
                harness.Run.Id, batch.BatchId, batch.Entries.Count, skipped: 0, failed: 0);
        }

        DateOnly windowEnd = (await harness.ReadStateAsync()).WindowEnd!.Value;
        int requestsBefore = harness.Source.Requests.Count;

        await harness.Orchestrator.ProcessAsync(
            harness.Run.Id, harness.Connection.Id, CancellationToken.None);

        harness.Source.Requests.Should().HaveCount(
            requestsBefore, "a fully read window must not be fetched again on redelivery");
        harness.Run.Status.Should().Be(JobStatus.Success);
        harness.Run.RecordsCreated.Should().Be(1500);

        SyncCheckpoint? watermark = harness.Watermarks.Find(
            harness.Connection.Id, JobType.WbFinanceReportImport);

        watermark.Should().NotBeNull();
        watermark!.LastChangedAt.Should().Be(
            new DateTimeOffset(windowEnd.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        watermark.LastJobRunId.Should().Be(harness.Run.Id);
    }

    [Fact]
    public async Task ProcessAsync_OzonWindowThrottledOnTheSecondPage_ResumesWithTheSameCursor()
    {
        Harness harness = new Harness("ozon", JobType.OzonFinanceAccrualsImport);

        harness.Source.RespondWith(_ => Page(10, "o1", nextCursor: "o2", isComplete: false));
        harness.Source.RespondWith(_ => Throttled(harness.Clock.GetUtcNow().AddSeconds(30)));

        await harness.Orchestrator.ProcessAsync(
            harness.Run.Id, harness.Connection.Id, CancellationToken.None);

        harness.Publisher.FinanceBatches.Should().HaveCount(1);
        (await harness.ReadStateAsync()).Cursor.Should().Be("o2");

        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        harness.Source.RespondWith(_ => Page(10, "o2", nextCursor: "o3", isComplete: false));
        harness.Source.RespondWith(_ => Page(10, "o3", nextCursor: null, isComplete: true));

        await harness.Orchestrator.ProcessAsync(
            harness.Run.Id, harness.Connection.Id, CancellationToken.None);

        harness.Source.Requests.Select(request => request.Cursor)
            .Should().Equal(null, "o2", "o2", "o3");
        harness.Publisher.FinanceBatches.Should().HaveCount(3);
        harness.Run.ExpectedBatchCount.Should().Be(3);
        (await harness.ReadStateAsync()).WindowCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessAsync_EmptyWindow_CompletesWithoutBatchesAndMovesTheWatermark()
    {
        Harness harness = new Harness("wildberries", JobType.WbFinanceReportImport);

        harness.Source.RespondWith(_ => Page(0, "none", nextCursor: null, isComplete: true));

        await harness.Orchestrator.ProcessAsync(
            harness.Run.Id, harness.Connection.Id, CancellationToken.None);

        harness.Publisher.FinanceBatches.Should().BeEmpty();
        harness.Run.Status.Should().Be(JobStatus.Success);
        harness.Run.CompletedAt.Should().NotBeNull();

        SyncCheckpoint? watermark = harness.Watermarks.Find(
            harness.Connection.Id, JobType.WbFinanceReportImport);

        watermark.Should().NotBeNull("an empty document still closes its window");
        watermark!.LastJobRunId.Should().Be(harness.Run.Id);
    }

    [Fact]
    public async Task ProcessAsync_InactiveConnection_FailsWithoutCallingTheChannel()
    {
        Harness harness = new Harness("wildberries", JobType.WbFinanceReportImport);
        harness.Connection.IsActive = false;

        await harness.Orchestrator.ProcessAsync(
            harness.Run.Id, harness.Connection.Id, CancellationToken.None);

        harness.Run.Status.Should().Be(JobStatus.Failed);
        harness.Run.Error.Should().Contain("inactive");
        harness.Source.Requests.Should().BeEmpty("a deactivated connection must not be called");
        harness.Publisher.FinanceBatches.Should().BeEmpty();
        harness.Scope.Entered.Should().Be(0);
    }

    private static FinanceEntriesPage Page(int count, string prefix, string? nextCursor, bool isComplete)
    {
        List<ChannelFinanceEntrySnapshot> entries = new();

        for (int index = 0; index < count; index++)
        {
            entries.Add(new ChannelFinanceEntrySnapshot
            {
                Source = FinanceEntrySource.WbReport,
                ReportInstanceKey = "1:2026-08-03",
                ExternalEntryId = $"{prefix}-{index}",
                ExternalLineId = $"srid-{prefix}-{index}",
                ExternalProductId = "5",
                Kind = FinanceEntryKind.Component,
                Type = FinanceEntryType.Sale,
                Amount = 100m,
                Direction = FinanceEntryDirection.Income,
                Currency = "RUB",
                DocumentDate = DocumentDate,
                PeriodFrom = new DateOnly(2026, 10, 26),
                PeriodTo = new DateOnly(2026, 11, 1),
            });
        }

        return new FinanceEntriesPage
        {
            Entries = entries,
            NextCursor = nextCursor,
            IsComplete = isComplete,
        };
    }

    private static FinanceEntriesPage Throttled(DateTimeOffset retryAt)
    {
        return new FinanceEntriesPage
        {
            Throttled = true,
            RetryAt = retryAt,
        };
    }

    private sealed class Harness
    {
        public Harness(string channelCode, JobType jobType)
        {
            SystemChannelTemplate template = new SystemChannelTemplate
            {
                Id = Guid.NewGuid(),
                Code = channelCode,
                DisplayName = channelCode,
                IsActive = true,
                CreatedAt = Clock.GetUtcNow(),
            };

            Connection = new Connection
            {
                Id = Guid.NewGuid(),
                ChannelId = Guid.NewGuid(),
                Name = "shop",
                IsActive = true,
                CreatedAt = Clock.GetUtcNow(),
                SystemChannelTemplate = template,
            };

            Run = new JobRun
            {
                Id = Guid.NewGuid(),
                ConnectionId = Connection.Id,
                JobType = jobType,
                Status = JobStatus.Pending,
                CreatedAt = Clock.GetUtcNow(),
                Connection = Connection,
            };

            JobRuns.Seed(Run);

            BatchImportFinalizer finalizer = new BatchImportFinalizer(
                JobRuns, Clock, NullLogger<BatchImportFinalizer>.Instance);

            ImportRunCompletion completion = new ImportRunCompletion(
                JobRuns, finalizer, Clock, NullLogger<ImportRunCompletion>.Instance);

            CheckpointStore = new FinanceImportCheckpointStore(Checkpoints, Clock);
            WatermarkWriter = new FinanceImportWatermarkWriter(
                Watermarks, Clock, NullLogger<FinanceImportWatermarkWriter>.Instance);

            Orchestrator = new FinanceImportOrchestrator(
                JobRuns,
                CheckpointStore,
                Watermarks,
                WatermarkWriter,
                Factory,
                Publisher,
                completion,
                Scope,
                new JobRunLifecycle(JobRuns, Clock),
                new FixedTenantTimeService(),
                new FixedTenantContext(TenantId),
                Options.Create(new FinanceImportOptions()),
                Clock,
                NullLogger<FinanceImportOrchestrator>.Instance);
        }

        public Guid TenantId { get; } = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        public FakeTimeProvider Clock { get; } = new FakeTimeProvider(
            new DateTimeOffset(2026, 11, 19, 15, 0, 0, TimeSpan.Zero));

        public FakeJobRunRepository JobRuns { get; } = new FakeJobRunRepository();

        public FakeJobCheckpointRepository Checkpoints { get; } = new FakeJobCheckpointRepository();

        public FakeSyncCheckpointRepository Watermarks { get; } = new FakeSyncCheckpointRepository();

        public ScriptedFinanceSource Source { get; } = new ScriptedFinanceSource();

        public ScriptedChannelApiClientFactory Factory => _factory ??= new ScriptedChannelApiClientFactory(Source);

        public RecordingEventPublisher Publisher { get; } = new RecordingEventPublisher();

        public RecordingJobRunExecutionScope Scope { get; } = new RecordingJobRunExecutionScope();

        public FinanceImportCheckpointStore CheckpointStore { get; }

        public FinanceImportWatermarkWriter WatermarkWriter { get; }

        public Connection Connection { get; }

        public JobRun Run { get; }

        public FinanceImportOrchestrator Orchestrator { get; }

        private ScriptedChannelApiClientFactory? _factory;

        public async Task<FinanceImportCheckpointPayload> ReadStateAsync()
        {
            FinanceImportCheckpointPayload? payload = await CheckpointStore
                .LoadAsync(Run.Id, CancellationToken.None);

            payload.Should().NotBeNull("every processed page is checkpointed");

            return payload!;
        }
    }
}
