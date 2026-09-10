namespace Delobytes.App.Backend.Catalog.Application.Queries.Suppliers.GetSuppliers;

public class GetSuppliersResponse
{
    public IReadOnlyList<SupplierItem> Items { get; set; } = new List<SupplierItem>();
}

public class SupplierItem
{
    public Guid Id { get; set; }

    public string Inn { get; set; } = default!;

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
