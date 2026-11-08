using System.Text.Json;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// Reads and writes the orders import checkpoint payload for one job run. Every write goes through
/// a phase-named method, so the payload's shape is owned here rather than restated by each caller.
/// </summary>
public sealed class OrdersImportCheckpointStore
{
    private readonly IJobCheckpointRepository _jobCheckpointRepository;
    private readonly TimeProvider _timeProvider;

    public OrdersImportCheckpointStore(
        IJobCheckpointRepository jobCheckpointRepository,
        TimeProvider timeProvider)
    {
        _jobCheckpointRepository = jobCheckpointRepository;
        _timeProvider = timeProvider;
    }

    internal async Task<OrdersImportCheckpointPayload?> LoadAsync(Guid jobRunId, CancellationToken cancellationToken)
    {
        JobCheckpoint? checkpoint = await _jobCheckpointRepository.GetByJobRunIdAsync(jobRunId, cancellationToken);

        return DeserializePayload(jobRunId, checkpoint?.Payload);
    }

    internal Task SaveStartedAsync(
        Guid jobRunId, OrdersImportContext context, CancellationToken cancellationToken)
    {
        return SaveAsync(
            jobRunId,
            new OrdersImportCheckpointPayload
            {
                Phase = OrdersImportCheckpointPhase.Started,
                TotalBatches = 0,
                WindowTo = context.WindowTo,
                IsBackfill = context.IsBackfill,
            },
            cancellationToken);
    }

    internal Task SaveProgressAsync(
        Guid jobRunId,
        OrdersImportContext context,
        RunCounters counters,
        BatchSequence batches,
        CancellationToken cancellationToken)
    {
        return SaveAsync(
            jobRunId,
            new OrdersImportCheckpointPayload
            {
                Phase = OrdersImportCheckpointPhase.InProgress,
                TotalBatches = batches.NextIndex,
                WindowTo = context.WindowTo,
                IsBackfill = context.IsBackfill,
                PagesFetched = counters.RequestsIssued,
            },
            cancellationToken);
    }

    internal Task SaveStoppedAsync(
        Guid jobRunId,
        OrdersImportContext context,
        RunCounters counters,
        BatchSequence batches,
        CancellationToken cancellationToken)
    {
        return SaveAsync(
            jobRunId,
            new OrdersImportCheckpointPayload
            {
                Phase = OrdersImportCheckpointPhase.Stopped,
                TotalBatches = batches.NextIndex,
                WindowTo = context.WindowTo,
                IsBackfill = context.IsBackfill,
                PagesFetched = counters.RequestsIssued,
            },
            cancellationToken);
    }

    internal Task SaveCompletedAsync(
        Guid jobRunId,
        OrdersImportContext context,
        RunCounters counters,
        BatchSequence batches,
        DateTimeOffset? maxChangedAt,
        bool isBackfillCompleted,
        DateTimeOffset? handoverWatermark,
        CancellationToken cancellationToken)
    {
        return SaveAsync(
            jobRunId,
            new OrdersImportCheckpointPayload
            {
                Phase = OrdersImportCheckpointPhase.FetchCompleted,
                TotalBatches = batches.NextIndex,
                WindowTo = context.WindowTo,
                IsBackfill = context.IsBackfill,
                IsBackfillCompleted = isBackfillCompleted,
                HandoverWatermark = handoverWatermark,
                MaxChangedAt = maxChangedAt,
                PagesFetched = counters.RequestsIssued,
            },
            cancellationToken);
    }

    private async Task SaveAsync(
        Guid jobRunId, OrdersImportCheckpointPayload payload, CancellationToken cancellationToken)
    {
        await _jobCheckpointRepository.UpsertAsync(
            new JobCheckpoint
            {
                JobRunId = jobRunId,
                Payload = JsonSerializer.Serialize(payload),
                UpdatedAt = _timeProvider.GetUtcNow(),
            },
            cancellationToken);
    }

    /// <summary>
    /// Returns null only when there is no checkpoint yet. A checkpoint that exists but cannot be
    /// read fails the run: treating it as absent restarts batch numbering at 0 and republishes
    /// BatchIds the aggregator has already recorded, which deduplicates them and silently drops
    /// the orders they carried.
    /// </summary>
    internal static OrdersImportCheckpointPayload? DeserializePayload(Guid jobRunId, string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<OrdersImportCheckpointPayload>(payload)
                ?? throw new InvalidOperationException(
                    $"Orders import checkpoint payload of JobRun {jobRunId} deserialised to null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Orders import checkpoint payload of JobRun {jobRunId} is corrupt: {exception.Message}",
                exception);
        }
    }

    /// <summary>
    /// Aggregate-side reading: an unreadable payload is reported as unknown instead of thrown,
    /// because failing one batch result would not repair the checkpoint and the run stays eligible
    /// for the watchdog. The deserialization itself is not reimplemented here.
    /// </summary>
    internal static OrdersImportCheckpointPayload? TryDeserializePayload(Guid jobRunId, string? payload)
    {
        try
        {
            return DeserializePayload(jobRunId, payload);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
