using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Domain.Entities;

/// <summary>
/// Common execution metadata for a single background job run, shared by every job type
/// (ProductsImport, OrdersImport, ...). Type-specific data lives in a separate table keyed
/// by <see cref="Id"/> (see ProductsImportJob, OrdersImportJob).
/// </summary>
public class JobRun : ITenantScoped, IRowVersionedEntity
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
    public JobStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user who requested this job.
    /// Null for system/scheduler-initiated jobs.
    /// </summary>
    public Guid? RequestedByUserId { get; set; }

    /// <summary>
    /// Gets or sets the date and time when this job run was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the job transitioned to Running.
    /// </summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the job reached a terminal status.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Gets or sets the error message if the job failed.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Gets or sets the number of records created during the run.
    /// </summary>
    public int RecordsCreated { get; set; }

    /// <summary>
    /// Gets or sets the number of records updated during the run.
    /// </summary>
    public int RecordsUpdated { get; set; }

    /// <summary>
    /// Gets or sets the number of records skipped during the run.
    /// </summary>
    public int RecordsSkipped { get; set; }

    /// <summary>
    /// Gets or sets the number of records that failed during the run.
    /// </summary>
    public int RecordsFailed { get; set; }

    /// <summary>
    /// Gets or sets the row version for optimistic concurrency (PostgreSQL xmin).
    /// </summary>
    public uint RowVersion { get; set; }

    /// <summary>
    /// Navigation property: the connection this job run belongs to.
    /// </summary>
    public Connection Connection { get; set; } = default!;
}
