using System.Text.Json;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

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

        return DeserializePayload(checkpoint?.Payload);
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

    internal static OrdersImportCheckpointPayload? DeserializePayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<OrdersImportCheckpointPayload>(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
