using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;
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
    public async Task LoadAsync_ValidCheckpoint_RoundTripsEveryField()
    {
        Guid jobRunId = Guid.NewGuid();
        DateTimeOffset windowTo = _timeProvider.GetUtcNow();
        DateTimeOffset watermark = windowTo.AddDays(-2);

        _jobCheckpointRepo
            .Setup(r => r.GetByJobRunIdAsync(jobRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobCheckpoint
            {
                JobRunId = jobRunId,
                Payload = System.Text.Json.JsonSerializer.Serialize(new OrdersImportCheckpointPayload
                {
                    TotalBatches = 7,
                    IsFetchCompleted = false,
                    WindowTo = windowTo,
                    PagesFetched = 12,
                    IsBackfill = true,
                    IsBackfillCompleted = false,
                    HandoverWatermark = watermark,
                    MaxChangedAt = null,
                }),
            });

        OrdersImportCheckpointPayload? payload = await CreateStore().LoadAsync(jobRunId, CancellationToken.None);

        payload.Should().NotBeNull();
        payload!.TotalBatches.Should().Be(7);
        payload.IsFetchCompleted.Should().BeFalse();
        payload.WindowTo.Should().Be(windowTo);
        payload.PagesFetched.Should().Be(12);
        payload.IsBackfill.Should().BeTrue();
        payload.IsBackfillCompleted.Should().BeFalse();
        payload.HandoverWatermark.Should().Be(watermark);
        payload.MaxChangedAt.Should().BeNull();
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

    [Fact]
    public async Task SaveAsync_PersistsSerialisedPayloadWithCurrentTimestamp()
    {
        Guid jobRunId = Guid.NewGuid();
        JobCheckpoint? saved = null;

        _jobCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<JobCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<JobCheckpoint, CancellationToken>((checkpoint, _) => saved = checkpoint)
            .Returns(Task.CompletedTask);

        await CreateStore().SaveAsync(
            jobRunId,
            new OrdersImportCheckpointPayload { TotalBatches = 3, IsFetchCompleted = true },
            CancellationToken.None);

        saved.Should().NotBeNull();
        saved!.JobRunId.Should().Be(jobRunId);
        saved.UpdatedAt.Should().Be(_timeProvider.GetUtcNow());

        OrdersImportCheckpointPayload? roundTrip =
            System.Text.Json.JsonSerializer.Deserialize<OrdersImportCheckpointPayload>(saved.Payload);

        roundTrip.Should().NotBeNull();
        roundTrip!.TotalBatches.Should().Be(3);
        roundTrip.IsFetchCompleted.Should().BeTrue();
    }
}
