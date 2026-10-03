using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface IComponentPriceRepository
{
    Task<ComponentPrice?> GetActiveByComponentIdAsync(Guid componentId, CancellationToken ct);

    Task<ComponentPrice?> GetLatestByComponentIdAsync(Guid componentId, CancellationToken ct);

    /// <summary>
    /// Returns the price version that was effective on <paramref name="asOf"/>: the latest version whose
    /// ValidFrom does not exceed the date. Deactivation is deliberately ignored, because a version
    /// replaced later was still the only effective one on an earlier date.
    /// </summary>
    /// <param name="componentId">Component to look up.</param>
    /// <param name="asOf">Date the price is resolved against.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The effective price version, or null when the component had no price by that date.</returns>
    Task<ComponentPrice?> GetEffectiveAtAsync(Guid componentId, DateOnly asOf, CancellationToken ct);

    void Add(ComponentPrice price);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
