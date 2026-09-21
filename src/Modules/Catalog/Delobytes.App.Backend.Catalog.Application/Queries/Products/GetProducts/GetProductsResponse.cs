using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProducts;

public class GetProductsResponse
{
    public List<ProductItem> Items { get; set; } = new();
}

public class ProductItem
{
    public Guid Id { get; set; }

    public string Sku { get; set; } = default!;

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public ProductStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public DateTimeOffset? DeletionRequestedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    // Placeholder for future marketplace import source — manual entry for now
    public string CreationSource { get; set; } = "Manual";
}
