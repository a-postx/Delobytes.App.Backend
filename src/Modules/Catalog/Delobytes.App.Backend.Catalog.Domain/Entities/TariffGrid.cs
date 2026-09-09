using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Represents a versioned logistics or storage tariff grid (WB by region, or fulfilment-centre by city).
/// Changes to tariff values are made by adding a new grid (new ValidFrom), not by editing the old one.
/// </summary>
public class TariffGrid : ITenantScoped
{
    public Guid Id { get; set; }

    /// <summary>The sales channel this tariff grid belongs to. Null means it is shared across channels.</summary>
    public Guid? ChannelId { get; set; }

    public string Name { get; set; } = default!;

    public TariffType TariffType { get; set; }

    /// <summary>Date from which this grid becomes effective.</summary>
    public DateOnly ValidFrom { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<TariffGridEntry> Entries { get; set; } = new List<TariffGridEntry>();
}
