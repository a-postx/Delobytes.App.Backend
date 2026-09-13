using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Represents the many-to-many link between a Product and a Component,
/// storing the quantity of that component consumed per unit of the product.
/// </summary>
public class ProductComponent : ITenantScoped
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public Guid ComponentId { get; set; }

    /// <summary>Quantity of the packaging component used per one finished product unit.</summary>
    public decimal Quantity { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Product Product { get; set; } = default!;

    public Component Component { get; set; } = default!;
}
