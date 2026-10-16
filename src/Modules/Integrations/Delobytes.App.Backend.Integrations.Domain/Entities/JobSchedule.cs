using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Domain.Entities;

/// <summary>
/// Tenant-scoped cron schedule for a background job type on a specific connection.
/// Read by SchedulerHostedService (Stage 11) to create Pending <see cref="JobRun"/> rows when due.
/// </summary>
public class JobSchedule : ITenantScoped
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the tenant identifier. Declared as a real property (not a shadow one) because
    /// the scheduler reads it directly to set up a per-tenant execution scope before resolving any
    /// tenant-scoped repository.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the connection identifier this schedule applies to.
    /// </summary>
    public Guid ConnectionId { get; set; }

    /// <summary>
    /// Gets or sets the job type this schedule triggers.
    /// </summary>
    public JobType JobType { get; set; }

    /// <summary>
    /// Gets or sets the 5-field cron expression (NCrontab) describing when this job type should run
    /// for this connection.
    /// </summary>
    public string CronExpression { get; set; } = default!;

    /// <summary>
    /// Gets or sets a value indicating whether this schedule is active. Disabled schedules are never
    /// returned by GetDueSchedulesAsync.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Gets or sets the date and time this schedule last fired (created a JobRun). Null if never.
    /// </summary>
    public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>
    /// Gets or sets the next due date and time, computed from <see cref="CronExpression"/> in the
    /// tenant's time zone. Null only until the first computation.
    /// </summary>
    public DateTimeOffset? NextRunAt { get; set; }

    /// <summary>
    /// Navigation property: the connection this schedule applies to.
    /// </summary>
    public Connection Connection { get; set; } = default!;
}
