namespace Delobytes.App.Backend.Catalog.Application.Queries.Suppliers.GetSupplier;

public class GetSupplierResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public string? ContactInfo { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
