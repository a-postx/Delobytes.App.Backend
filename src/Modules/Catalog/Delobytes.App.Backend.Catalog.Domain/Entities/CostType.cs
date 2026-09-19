using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// A named category of channel-level cost (e.g. logistics, fulfilment fee, packaging surcharge).
/// Acts as a shared lookup: users create types once and reference them from ProductChannelCost.
/// </summary>
public class CostType : ITenantScoped
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public ICollection<ProductChannelCost> ProductChannelCosts { get; set; } = new List<ProductChannelCost>();
}
