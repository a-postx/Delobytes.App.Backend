using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Represents a material or component.
/// Carries descriptive data only; price and supplier live in <see cref="ComponentPrice"/>
/// versions so that historical cost calculations remain intact.
/// </summary>
public class Component : ITenantScoped
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public Unit Unit { get; set; }

    /// <summary>Whether the logical component is archived. Does not depend on price versions.</summary>
    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<ComponentPrice> Prices { get; set; } = new List<ComponentPrice>();

    public ICollection<ProductComponent> ProductComponents { get; set; } = new List<ProductComponent>();
}
