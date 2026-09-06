using Delobytes.App.Backend.Identity.Domain.Interfaces;

namespace Delobytes.App.Backend.Integrations.Domain.Entities;

/// <summary>
/// Represents a tenant-scoped connection to a marketplace channel.
/// </summary>
public class Connection : ITenantScoped
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the channel identifier.
    /// </summary>
    public Guid ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the user-friendly connection name.
    /// </summary>
    public string Name { get; set; } = default!;

    /// <summary>
    /// Gets or sets the API key.
    /// </summary>
    public string ApiKey { get; set; } = default!;

    /// <summary>
    /// Gets or sets the optional API secret.
    /// </summary>
    public string? ApiSecret { get; set; }

    /// <summary>
    /// Gets or sets optional JSON-serialized settings.
    /// </summary>
    public string? Settings { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the connection is active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets the date and time of the last synchronization.
    /// </summary>
    public DateTimeOffset? LastSyncAt { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the connection was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the connection was last updated.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Navigation property: the channel template.
    /// </summary>
    public SystemChannelTemplate Channel { get; set; } = default!;

    /// <summary>
    /// Navigation property: sync jobs for this connection.
    /// </summary>
    public ICollection<SyncJob> SyncJobs { get; set; } = new List<SyncJob>();
}
