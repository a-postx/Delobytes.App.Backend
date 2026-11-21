namespace Delobytes.App.Backend.Finance.Application.ReadModels;

/// <summary>
/// A read row that carries a catalogue product identifier and can be labelled with its SKU.
/// Implemented by the P&amp;L views so the label is applied by one shared step instead of a copy
/// per query.
/// </summary>
public interface IPnlSkuLabelTarget
{
    /// <summary>Gets the linked catalogue product, or null when the row has no product link.</summary>
    public Guid? ProductId { get; }

    /// <summary>Gets or sets the resolved SKU of that product.</summary>
    public string? Sku { get; set; }
}
