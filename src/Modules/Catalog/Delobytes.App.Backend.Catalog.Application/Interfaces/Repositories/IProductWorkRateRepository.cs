using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface IProductWorkRateRepository
{
    Task<ProductWorkRate?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<ProductWorkRate>> GetAllAsync(CancellationToken ct);

    Task<IReadOnlyList<ProductWorkRate>> GetByProductIdAsync(Guid productId, CancellationToken ct);

    /// <summary>
    /// Returns the assembly output rate version that was effective on <paramref name="asOf"/>.
    /// Deactivation is deliberately ignored, so a soft-deleted rate keeps resolving for dates on
    /// which it was the effective one. Pass the current date to honour deletion in new calculations.
    /// </summary>
    /// <param name="productId">Product to look up.</param>
    /// <param name="asOf">Date the rate is resolved against.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The effective rate version, or null when none existed by that date.</returns>
    Task<ProductWorkRate?> GetEffectiveAtAsync(Guid productId, DateOnly asOf, CancellationToken ct);

    void Add(ProductWorkRate rate);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
