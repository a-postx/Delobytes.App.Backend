namespace Delobytes.App.Backend.Catalog.Application.Queries.Components;

/// <summary>
/// Active price version of a component as exposed to the client.
/// </summary>
public class ComponentPriceDto
{
    public Guid Id { get; set; }

    public decimal PricePerUnit { get; set; }

    public Guid? SupplierId { get; set; }

    public string? SupplierName { get; set; }

    /// <summary>Effective date in yyyy-MM-dd format.</summary>
    public string ValidFrom { get; set; } = default!;
}
