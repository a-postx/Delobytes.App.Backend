namespace Delobytes.App.Backend.Catalog.Application.Queries.PackagingComponents;

/// <summary>
/// Active price version of a packaging component as exposed to the client.
/// </summary>
public class PackagingComponentPriceDto
{
    public Guid Id { get; set; }

    public decimal PricePerUnit { get; set; }

    public Guid? SupplierId { get; set; }

    public string? SupplierName { get; set; }

    /// <summary>Effective date in yyyy-MM-dd format.</summary>
    public string ValidFrom { get; set; } = default!;
}
