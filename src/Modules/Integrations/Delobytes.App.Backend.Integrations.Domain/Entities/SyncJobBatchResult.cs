using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Integrations.Domain.Entities;

/// <summary>
/// Stores the result of a single product import batch for idempotent aggregation.
/// MessageId (from MassTransit) serves as the deduplication key.
/// </summary>
public class SyncJobBatchResult : ITenantScoped
{
    public Guid Id { get; set; }

    public Guid SyncJobId { get; set; }

    /// <summary>
    /// MassTransit MessageId — used as idempotency key to prevent double-counting on redelivery.
    /// </summary>
    public Guid MessageId { get; set; }

    public int RecordsProcessed { get; set; }

    public int RecordsCreated { get; set; }

    public int RecordsUpdated { get; set; }

    public int RecordsSkipped { get; set; }

    public int RecordsFailed { get; set; }

    public string? ErrorMessage { get; set; }

    public bool IsLastBatch { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public SyncJob SyncJob { get; set; } = default!;
}
