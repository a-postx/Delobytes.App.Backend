namespace Delobytes.App.Backend.Integrations.Domain.Entities;

/// <summary>
/// Represents a system channel template (marketplace integration template).
/// </summary>
public class SystemChannelTemplate
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the unique code (e.g., "wildberries", "ozon").
    /// </summary>
    public string Code { get; set; } = default!;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string DisplayName { get; set; } = default!;

    /// <summary>
    /// Gets or sets the API base URL.
    /// </summary>
    public string ApiBaseUrl { get; set; } = default!;

    /// <summary>
    /// Gets or sets the API version.
    /// </summary>
    public string ApiVersion { get; set; } = default!;

    /// <summary>
    /// Gets or sets the optional description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the channel is active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the template was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Navigation property: connections using this channel.
    /// </summary>
    public ICollection<Connection> Connections { get; set; } = new List<Connection>();
}
