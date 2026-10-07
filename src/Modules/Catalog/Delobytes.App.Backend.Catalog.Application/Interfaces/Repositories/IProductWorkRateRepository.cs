using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface IProductWorkRateRepository
{
    Task<ProductWorkRate?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<ProductWorkRate>> GetAllAsync(CancellationToken ct);

    Task<IReadOnlyList<ProductWorkRate>> GetByProductIdAsync(Guid productId, CancellationToken ct);

    /// <summary>
    /// Returns the version currently in force for the product (IsActive = true), so that a new
    /// version appended through POST can supersede it. At most one row is expected to match;
    /// see the repository implementation for the tie-break used if more than one ever does.
    /// </summary>
    /// <param name="productId">Product to look up.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The active version, or null when the product currently has none.</returns>
    Task<ProductWorkRate?> GetActiveByProductIdAsync(Guid productId, CancellationToken ct);

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

    Task<IReadOnlyList<Guid>> GetProductIdsByWorkRateIdAsync(Guid workRateId, CancellationToken ct);

    void Add(ProductWorkRate rate);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
