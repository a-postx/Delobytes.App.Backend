using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Represents a barcode that identifies a product in a specific system.
/// A product typically has one barcode, but can have several from different marketplaces
/// or marking systems.
/// </summary>
public class ProductBarcode : ITenantScoped, IAuditableEntity, IRowVersionedEntity
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public string Value { get; set; } = default!;

    /// <summary>
    /// Barcode symbology, e.g. <see cref="BarcodeTypes.Ean13"/>. Not a channel code: the channel's
    /// own copy of a barcode and its reported type live on <see cref="ChannelProductBarcode"/>.
    /// Stored as free text so a new symbology needs no migration.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>True for the barcode printed on the product label.</summary>
    public bool IsDefault { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public uint RowVersion { get; set; }

    public Product Product { get; set; } = default!;
}
