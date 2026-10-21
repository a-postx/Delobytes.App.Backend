using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Integrations.Domain.Entities;

/// <summary>
/// Per-connection progress cursor for the day-by-day Wildberries orders backfill. Tracks the
/// backwards and catch-up cursors across pod restarts and throttled runs. Seeds <see cref="SyncCheckpoint"/> once the backfill completes.
/// </summary>
public class OrdersBackfillState : ITenantScoped
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the connection identifier this backfill belongs to.
    /// </summary>
    public Guid ConnectionId { get; set; }

    /// <summary>
    /// Gets or sets the next calendar day (MSK, 00:00) still to be fetched, walking backwards.
    /// </summary>
    public DateTimeOffset NextDayToFetch { get; set; }

    /// <summary>
    /// Gets or sets the oldest day still in scope; the backfill ends once the cursor passes it.
    /// </summary>
    public DateTimeOffset OldestDayInScope { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether both the backwards walk and catch-up completed.
    /// </summary>
    public bool IsCompleted { get; set; }

    public DateTimeOffset? CatchUpNextDay { get; set; }

    public DateTimeOffset? HandoverWatermark { get; set; }

    /// <summary>
    /// Gets or sets the date and time the backfill completed. Null until <see cref="IsCompleted"/>
    /// is true.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Gets or sets the legacy observed watermark. Retained for stored data compatibility.
    /// </summary>
    public DateTimeOffset? MaxObservedLastChangeDate { get; set; }

    /// <summary>
    /// Navigation property: the connection this backfill belongs to.
    /// </summary>
    public Connection Connection { get; set; } = default!;
}
