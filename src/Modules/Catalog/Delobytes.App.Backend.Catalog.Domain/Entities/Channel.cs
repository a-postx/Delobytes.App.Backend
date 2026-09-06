using Delobytes.App.Backend.Identity.Domain.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Represents a sales or marketplace channel where products can be sold.
/// Can be based on a system template or completely custom.
/// </summary>
public class Channel : ITenantScoped
{
    /// <summary>
    /// Gets or sets the channel unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the system channel template identifier (nullable).
    /// If null, the channel is custom (IsCustom = true).
    /// </summary>
    public Guid? SystemChannelTemplateId { get; set; }

    /// <summary>
    /// Gets or sets the channel name.
    /// For system channels, copied from template; for custom channels, set by user.
    /// </summary>
    public string Name { get; set; } = default!;

    /// <summary>
    /// Gets or sets the custom API URL (only for custom channels).
    /// </summary>
    public string? CustomApiUrl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is a custom channel.
    /// True if SystemChannelTemplateId is null.
    /// </summary>
    public bool IsCustom { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the channel is active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the channel was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the channel was last updated.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Navigation property: products linked to this channel.
    /// </summary>
    public ICollection<ChannelProduct> ChannelProducts { get; set; } = new List<ChannelProduct>();
}
