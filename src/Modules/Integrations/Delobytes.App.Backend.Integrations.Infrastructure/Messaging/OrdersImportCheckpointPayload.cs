namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging;

/// <summary>
/// JobCheckpoint payload shared by the orders import orchestrator and its aggregator.
/// Written before any batch is published so the aggregator always knows the expected batch count.
/// </summary>
public sealed record OrdersImportCheckpointPayload
{
    public int TotalBatches { get; init; }

    public DateTimeOffset? MaxChangedAt { get; init; }

    public DateTimeOffset WindowTo { get; init; }

    public int PagesFetched { get; init; }
}
