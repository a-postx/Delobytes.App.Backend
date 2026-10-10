using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Sales.Domain.Enums;

namespace Delobytes.App.Backend.Sales.Domain.Entities;

/// <summary>
/// A return registered against a specific order line.
/// </summary>
public class Return : ITenantScoped
{
    /// <summary>
    /// Gets or sets the return unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the order line the goods came back from.
    /// </summary>
    public Guid OrderLineId { get; set; }

    /// <summary>
    /// Gets or sets the channel-side return identity, where one exists.
    /// </summary>
    public string? ExternalReturnId { get; set; }

    /// <summary>
    /// Gets or sets the quantity of units returned.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Gets or sets the date the return was registered by the channel.
    /// </summary>
    public DateTimeOffset ReturnDate { get; set; }

    /// <summary>
    /// Gets or sets the canonical return kind.
    /// </summary>
    public ReturnKind Kind { get; set; } = ReturnKind.Other;

    /// <summary>
    /// Gets or sets the verbatim channel reason.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Gets or sets the refund amount. Null means the channel did not report it — not zero.
    /// </summary>
    public decimal? RefundAmount { get; set; }

    /// <summary>
    /// Gets or sets the refund currency, where the channel reports one.
    /// </summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Gets or sets where <see cref="RefundAmount"/> came from.
    /// </summary>
    public ValueSource RefundSource { get; set; } = ValueSource.Unknown;

    /// <summary>
    /// Gets or sets the identifier of the raw payload this return was built from.
    /// </summary>
    public Guid? RawDataId { get; set; }

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the order line the goods came back from.
    /// </summary>
    public OrderLine OrderLine { get; set; } = default!;
}
