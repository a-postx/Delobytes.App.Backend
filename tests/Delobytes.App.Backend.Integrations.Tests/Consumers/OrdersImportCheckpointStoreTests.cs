using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Application.Orders;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

public class OrdersImportCheckpointStoreTests
{
    private readonly FakeTimeProvider _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero));
    private readonly Mock<IJobCheckpointRepository> _jobCheckpointRepo = new();

    private OrdersImportCheckpointStore CreateStore()
    {
        return new OrdersImportCheckpointStore(_jobCheckpointRepo.Object, _timeProvider);
    }

    private JobCheckpoint? CaptureSavedCheckpoint()
    {
        JobCheckpoint? saved = null;

        _jobCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<JobCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<JobCheckpoint, CancellationToken>((checkpoint, _) => saved = checkpoint)
            .Returns(Task.CompletedTask);

        return saved;
    }

    [Fact]
    public async Task LoadAsync_NoCheckpoint_ReturnsNull()
    {
        Guid jobRunId = Guid.NewGuid();

        _jobCheckpointRepo
            .Setup(r => r.GetByJobRunIdAsync(jobRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobCheckpoint?)null);

        OrdersImportCheckpointPayload? payload = await CreateStore().LoadAsync(jobRunId, CancellationToken.None);

        payload.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_CurrentPayload_RoundTripsEveryField()
    {
        Guid jobRunId = Guid.NewGuid();
        DateTimeOffset windowTo = _timeProvider.GetUtcNow();
        DateTimeOffset watermark = windowTo.AddDays(-2);

        _jobCheckpointRepo
            .Setup(r => r.GetByJobRunIdAsync(jobRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobCheckpoint
            {
                JobRunId = jobRunId,
                Payload = JsonSerializer.Serialize(new OrdersImportCheckpointPayload
                {
                    Phase = OrdersImportCheckpointPhase.FetchCompleted,
                    TotalBatches = 7,
                    WindowTo = windowTo,
                    PagesFetched = 12,
                    IsBackfill = true,
                    IsBackfillCompleted = true,
                    HandoverWatermark = watermark,
                    MaxChangedAt = null,
                }),
            });

        OrdersImportCheckpointPayload? payload = await CreateStore().LoadAsync(jobRunId, CancellationToken.None);

        payload.Should().NotBeNull();
        payload!.Phase.Should().Be(OrdersImportCheckpointPhase.FetchCompleted);
        payload.TotalBatches.Should().Be(7);
        payload.WindowTo.Should().Be(windowTo);
        payload.PagesFetched.Should().Be(12);
        payload.IsBackfill.Should().BeTrue();
        payload.IsBackfillCompleted.Should().BeTrue();
        payload.HandoverWatermark.Should().Be(watermark);
        payload.MaxChangedAt.Should().BeNull();
    }

    [Theory]
    [InlineData(true, OrdersImportCheckpointPhase.FetchCompleted)]
    [InlineData(false, OrdersImportCheckpointPhase.InProgress)]
    public void DeserializePayload_LegacyIsFetchCompleted_DerivesThePhase(
        bool isFetchCompleted, OrdersImportCheckpointPhase expectedPhase)
    {
        string legacyJson =
            "{\"TotalBatches\":5,\"IsFetchCompleted\":" + (isFetchCompleted ? "true" : "false") + ","
            + "\"WindowTo\":\"2026-10-21T09:00:00+00:00\",\"IsBackfill\":true}";

        OrdersImportCheckpointPayload? payload =
            OrdersImportCheckpointStore.DeserializePayload(Guid.NewGuid(), legacyJson);

        payload.Should().NotBeNull();
        payload!.Phase.Should().Be(expectedPhase);
        payload.TotalBatches.Should().Be(5);
        payload.IsBackfill.Should().BeTrue();
    }

    [Fact]
    public void DeserializePayload_LegacyWithoutIsFetchCompleted_DerivesInProgress()
    {
        string legacyJson = "{\"TotalBatches\":2,\"WindowTo\":\"2026-10-21T09:00:00+00:00\"}";

        OrdersImportCheckpointPayload? payload =
            OrdersImportCheckpointStore.DeserializePayload(Guid.NewGuid(), legacyJson);

        payload.Should().NotBeNull();
        payload!.Phase.Should().Be(OrdersImportCheckpointPhase.InProgress);
        payload.TotalBatches.Should().Be(2);
    }

    [Fact]
    public void Serialize_WritesPhaseAndNeverWritesTheLegacyFlag()
    {
        string json = JsonSerializer.Serialize(new OrdersImportCheckpointPayload
        {
            Phase = OrdersImportCheckpointPhase.Stopped,
            TotalBatches = 4,
            WindowTo = _timeProvider.GetUtcNow(),
            IsBackfill = false,
        });

        json.Should().Contain("\"Phase\":2");
        json.Should().NotContain("IsFetchCompleted");
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("null")]
    public async Task LoadAsync_UnreadableCheckpoint_FailsInsteadOfRestartingBatchNumbering(string storedPayload)
    {
        Guid jobRunId = Guid.NewGuid();

        _jobCheckpointRepo
            .Setup(r => r.GetByJobRunIdAsync(jobRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobCheckpoint { JobRunId = jobRunId, Payload = storedPayload });

        // Returning null here would look like "no checkpoint yet": the orchestrator would restart
        // batch numbering at 0 and republish BatchIds the aggregator has already recorded, which it
        // deduplicates - silently dropping the orders those batches carried. The read has to fail.
        Func<Task> act = async () => await CreateStore().LoadAsync(jobRunId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{jobRunId}*");
    }

    [Fact]
    public async Task SaveStartedAsync_WritesTheStartedPhase()
    {
        Guid jobRunId = Guid.NewGuid();
        OrdersImportContext context = OrdersImportContextBuilder.Build(isBackfill: true);
        JobCheckpoint? saved = null;

        _jobCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<JobCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<JobCheckpoint, CancellationToken>((checkpoint, _) => saved = checkpoint)
            .Returns(Task.CompletedTask);

        await CreateStore().SaveStartedAsync(jobRunId, context, CancellationToken.None);

        OrdersImportCheckpointPayload payload = ReadPayload(saved);
        payload.Phase.Should().Be(OrdersImportCheckpointPhase.Started);
        payload.TotalBatches.Should().Be(0);
        payload.WindowTo.Should().Be(context.WindowTo);
        payload.IsBackfill.Should().BeTrue();
        payload.PagesFetched.Should().Be(0);
    }

    [Fact]
    public async Task SaveProgressAsync_WritesTheInProgressPhaseWithCounters()
    {
        Guid jobRunId = Guid.NewGuid();
        OrdersImportContext context = OrdersImportContextBuilder.Build();
        RunCounters counters = new RunCounters { RequestsIssued = 12 };
        BatchSequence batches = new BatchSequence(3);
        JobCheckpoint? saved = null;

        _jobCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<JobCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<JobCheckpoint, CancellationToken>((checkpoint, _) => saved = checkpoint)
            .Returns(Task.CompletedTask);

        await CreateStore().SaveProgressAsync(jobRunId, context, counters, batches, CancellationToken.None);

        OrdersImportCheckpointPayload payload = ReadPayload(saved);
        payload.Phase.Should().Be(OrdersImportCheckpointPhase.InProgress);
        payload.TotalBatches.Should().Be(3);
        payload.PagesFetched.Should().Be(12);
    }

    [Fact]
    public async Task SaveStoppedAsync_WritesTheStoppedPhaseWithCounters()
    {
        Guid jobRunId = Guid.NewGuid();
        OrdersImportContext context = OrdersImportContextBuilder.Build();
        RunCounters counters = new RunCounters { RequestsIssued = 5 };
        BatchSequence batches = new BatchSequence(2);
        JobCheckpoint? saved = null;

        _jobCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<JobCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<JobCheckpoint, CancellationToken>((checkpoint, _) => saved = checkpoint)
            .Returns(Task.CompletedTask);

        await CreateStore().SaveStoppedAsync(jobRunId, context, counters, batches, CancellationToken.None);

        OrdersImportCheckpointPayload payload = ReadPayload(saved);
        payload.Phase.Should().Be(OrdersImportCheckpointPhase.Stopped);
        payload.TotalBatches.Should().Be(2);
        payload.PagesFetched.Should().Be(5);
    }

    [Fact]
    public async Task SaveCompletedAsync_WritesTheFetchCompletedPhaseAndBranchFields()
    {
        Guid jobRunId = Guid.NewGuid();
        OrdersImportContext context = OrdersImportContextBuilder.Build(isBackfill: true);
        DateTimeOffset handover = _timeProvider.GetUtcNow().AddHours(-1);
        JobCheckpoint? saved = null;

        _jobCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<JobCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<JobCheckpoint, CancellationToken>((checkpoint, _) => saved = checkpoint)
            .Returns(Task.CompletedTask);

        await CreateStore().SaveCompletedAsync(
            jobRunId,
            context,
            new RunCounters { RequestsIssued = 8 },
            new BatchSequence(4),
            maxChangedAt: null,
            isBackfillCompleted: true,
            handoverWatermark: handover,
            CancellationToken.None);

        OrdersImportCheckpointPayload payload = ReadPayload(saved);
        payload.Phase.Should().Be(OrdersImportCheckpointPhase.FetchCompleted);
        payload.TotalBatches.Should().Be(4);
        payload.PagesFetched.Should().Be(8);
        payload.IsBackfill.Should().BeTrue();
        payload.IsBackfillCompleted.Should().BeTrue();
        payload.HandoverWatermark.Should().Be(handover);
        payload.MaxChangedAt.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_PersistsWithCurrentTimestamp()
    {
        Guid jobRunId = Guid.NewGuid();
        JobCheckpoint? saved = null;

        _jobCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<JobCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<JobCheckpoint, CancellationToken>((checkpoint, _) => saved = checkpoint)
            .Returns(Task.CompletedTask);

        await CreateStore().SaveStartedAsync(
            jobRunId, OrdersImportContextBuilder.Build(), CancellationToken.None);

        saved.Should().NotBeNull();
        saved!.JobRunId.Should().Be(jobRunId);
        saved.UpdatedAt.Should().Be(_timeProvider.GetUtcNow());
    }

    [Fact]
    public void DeserializePayload_WhitespacePayload_ReturnsNull()
    {
        OrdersImportCheckpointPayload? payload = OrdersImportCheckpointStore.DeserializePayload(Guid.NewGuid(), "   ");

        payload.Should().BeNull();
    }

    [Fact]
    public void DeserializePayload_CorruptPayload_ReportsTheJobRun()
    {
        Guid jobRunId = Guid.NewGuid();

        Action act = () => OrdersImportCheckpointStore.DeserializePayload(jobRunId, "{ broken");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{jobRunId}*corrupt*");
    }

    private static OrdersImportCheckpointPayload ReadPayload(JobCheckpoint? checkpoint)
    {
        checkpoint.Should().NotBeNull();
        OrdersImportCheckpointPayload? payload =
            JsonSerializer.Deserialize<OrdersImportCheckpointPayload>(checkpoint!.Payload);

        payload.Should().NotBeNull();

        return payload!;
    }
}
