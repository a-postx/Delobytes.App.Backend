using Delobytes.App.Backend.Identity.Domain.Interfaces;

namespace Delobytes.App.Backend.Sales.Domain.Entities;

/// <summary>
/// Represents a return of an order.
/// </summary>
public class Return : ITenantScoped
{
    /// <summary>
    /// Gets or sets the return unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the order identifier.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Gets or sets the quantity of items returned.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the return was processed.
    /// </summary>
    public DateTimeOffset ReturnDate { get; set; }

    /// <summary>
    /// Gets or sets the reason for the return.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Gets or sets the refund amount.
    /// </summary>
    public decimal RefundAmount { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the return was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Navigation property: the order.
    /// </summary>
    public Order Order { get; set; } = default!;
}
