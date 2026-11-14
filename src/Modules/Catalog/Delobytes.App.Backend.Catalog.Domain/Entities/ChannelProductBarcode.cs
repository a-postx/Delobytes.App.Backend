using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Represents a barcode a sales channel reports for one of its cards.
/// </summary>
/// <remarks>
/// <para>
/// Channel-owned data: the import replaces the whole set of a card on every run, so a barcode the
/// channel stopped reporting disappears here. This is deliberately not
/// <see cref="ProductBarcode"/>: the canonical barcode identifies the product across the whole
/// tenant and stays unique per tenant, while this set only has to be free of duplicates within one
/// card. When the two agree, the canonical barcode is simply also present here; when they diverge,
/// that difference is information and is kept.
/// </para>
/// <para>
/// A repeated barcode is stored, not resolved. A channel may report one value for two of its cards,
/// and both cards keep their own copy here. Such a value does not identify a product on its own, so
/// the import treats it as a match only when that match leads somewhere: a card matching a product
/// that already holds a link to this channel is skipped rather than attached, and the repeated
/// barcode makes no second product appear. The catalogue therefore ends up with one product for a
/// value the channel reuses, which is the cost of the canonical set being unique per tenant.
/// </para>
/// </remarks>
public class ChannelProductBarcode : ITenantScoped
{
    /// <summary>Gets or sets the unique identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the link of the card this barcode belongs to.</summary>
    public Guid ChannelProductId { get; set; }

    /// <summary>Gets or sets the barcode value exactly as the channel reports it.</summary>
    public string Value { get; set; } = default!;

    /// <summary>
    /// Gets or sets the barcode system the channel reports (<c>EAN13</c>, <c>BARCODE</c>, ...).
    /// Free text, so a new marketplace needs no migration.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>Gets or sets the date and time when this row was first written.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets the date and time when this row was last acknowledged by an import.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Navigation property: the card the barcode belongs to.</summary>
    public ChannelProduct ChannelProduct { get; set; } = default!;
}
