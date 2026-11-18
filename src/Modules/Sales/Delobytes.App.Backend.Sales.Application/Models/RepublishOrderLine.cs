using Delobytes.App.Backend.Sales.Contracts.Events;

namespace Delobytes.App.Backend.Sales.Application.Models;

/// <summary>
/// A flat page row of the order line re-publish. Carries exactly what
/// <see cref="ChangedOrderLine"/> needs, so the paging query does not load entities.
/// </summary>
public sealed class RepublishOrderLine
{
    /// <summary>Gets or sets the order line identifier.</summary>
    public Guid OrderLineId { get; set; }

    /// <summary>Gets or sets the owning order identifier.</summary>
    public Guid OrderId { get; set; }

    /// <summary>Gets or sets the sales channel identifier.</summary>
    public Guid ChannelId { get; set; }

    /// <summary>Gets or sets the channel-side order identity.</summary>
    public string ExternalOrderId { get; set; } = default!;

    /// <summary>Gets or sets the channel-side unit identity.</summary>
    public string? ExternalLineId { get; set; }

    /// <summary>Gets or sets the channel-side product identity.</summary>
    public string ExternalProductId { get; set; } = default!;

    /// <summary>Gets or sets the resolved catalogue channel product.</summary>
    public Guid? ChannelProductId { get; set; }

    /// <summary>Gets or sets the line quantity.</summary>
    public int Quantity { get; set; }

    /// <summary>Gets or sets the order placement instant.</summary>
    public DateTimeOffset OrderDate { get; set; }

    /// <summary>Gets or sets the line currency.</summary>
    public string Currency { get; set; } = default!;

    /// <summary>Gets or sets a value indicating whether the line is cancelled.</summary>
    public bool IsCancelled { get; set; }
}
