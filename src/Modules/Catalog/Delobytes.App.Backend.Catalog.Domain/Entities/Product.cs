using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Represents a product in the catalog.
/// </summary>
public class Product : ITenantScoped
{
    public Guid Id { get; set; }

    /// <summary>Internal SKU code.</summary>
    public string Sku { get; set; } = default!;

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    /// <summary>Box dimensions in cm, used to calculate volume for logistics formulas.</summary>
    public decimal LengthCm { get; set; }

    public decimal WidthCm { get; set; }

    public decimal HeightCm { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public ICollection<ChannelProduct> ChannelProducts { get; set; } = new List<ChannelProduct>();

    public ICollection<ProductComponent> ProductComponents { get; set; } = new List<ProductComponent>();

    public ICollection<ProductPackagingComponent> ProductPackagingComponents { get; set; } = new List<ProductPackagingComponent>();

    public ICollection<ProductChannelInput> ProductChannelInputs { get; set; } = new List<ProductChannelInput>();
}
