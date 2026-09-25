using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// A versioned set of parameters for one sales channel.
/// Editing any parameter creates a new record with the new ValidFrom date;
/// prior records are kept unchanged so historical snapshots remain correct.
/// </summary>
public class ChannelParameterSet : ITenantScoped
{
    public Guid Id { get; set; }

    public Guid ChannelId { get; set; }

    /// <summary>Date from which this parameter set becomes effective.</summary>
    public DateOnly ValidFrom { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Channel Channel { get; set; } = default!;
}
