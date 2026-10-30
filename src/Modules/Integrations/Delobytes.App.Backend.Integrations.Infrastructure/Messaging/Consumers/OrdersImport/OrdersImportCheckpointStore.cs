using System.Text.Json;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Reads and writes the orders import checkpoint payload for one job run.
/// </summary>
internal sealed class OrdersImportCheckpointStore
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

    public async Task<OrdersImportCheckpointPayload?> LoadAsync(Guid jobRunId, CancellationToken cancellationToken)
    {
        JobCheckpoint? checkpoint = await _jobCheckpointRepository.GetByJobRunIdAsync(jobRunId, cancellationToken);

        return DeserializePayload(jobRunId, checkpoint?.Payload);
    }

    public async Task SaveAsync(Guid jobRunId, OrdersImportCheckpointPayload payload, CancellationToken cancellationToken)
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
}
