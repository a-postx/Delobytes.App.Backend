using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Domain.Entities;

/// <summary>
/// Represents a system-level API endpoint configuration for a specific channel operation type.
/// </summary>
public class SystemChannelEndpoint
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the system channel template identifier (FK).
    /// </summary>
    public Guid SystemChannelTemplateId { get; set; }

    /// <summary>
    /// Gets or sets the type of operations this endpoint handles.
    /// </summary>
    public ChannelEndpointType EndpointType { get; set; }

    /// <summary>
    /// Gets or sets the base URL for this endpoint type.
    /// </summary>
    public string BaseUrl { get; set; } = default!;

    /// <summary>
    /// Gets or sets the optional description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the endpoint is active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the endpoint was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Navigation property: the parent system channel template.
    /// </summary>
    public SystemChannelTemplate SystemChannelTemplate { get; set; } = default!;
}
