using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Domain.Entities;

/// <summary>
/// Represents a tenant-scoped synchronization job.
/// </summary>
public class SyncJob : ITenantScoped
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the connection identifier.
    /// </summary>
    public Guid ConnectionId { get; set; }

    /// <summary>
    /// Gets or sets the job type.
    /// </summary>
    public JobType JobType { get; set; }

    /// <summary>
    /// Gets or sets the job status.
    /// </summary>
    public SyncJobStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the start of the date range for synchronization.
    /// </summary>
    public DateTimeOffset DateRangeFrom { get; set; }

    /// <summary>
    /// Gets or sets the end of the date range for synchronization.
    /// </summary>
    public DateTimeOffset DateRangeTo { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the job started.
    /// </summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the job completed.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Gets or sets the error message if the job failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the number of records processed.
    /// </summary>
    public int RecordsProcessed { get; set; }

    /// <summary>
    /// Gets or sets the number of records imported.
    /// </summary>
    public int RecordsImported { get; set; }

    /// <summary>
    /// Navigation property: the connection.
    /// </summary>
    public Connection Connection { get; set; } = default!;

    /// <summary>
    /// Navigation property: raw API responses for this job.
    /// </summary>
    public ICollection<RawApiResponse> RawApiResponses { get; set; } = new List<RawApiResponse>();
}
