using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// A manually entered cost item for a specific product in a specific sales channel.
/// Each row links a product, a channel, a cost category (CostType), and the monetary amount.
/// </summary>
public class ProductChannelCost : ITenantScoped
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public Guid ChannelId { get; set; }

    public Guid CostTypeId { get; set; }

    /// <summary>Cost amount in the tenant's currency.</summary>
    public decimal Amount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Product Product { get; set; } = default!;

    public Channel Channel { get; set; } = default!;

    public CostType CostType { get; set; } = default!;
}
