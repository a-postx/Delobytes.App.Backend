namespace Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;

/// <summary>
/// Calculates the cost of a product as it was on a given date.
/// Implementations must be read-only: they resolve versioned inputs but never persist anything,
/// because the same service is also called from the snapshot writer.
/// </summary>
public interface ICostCalculator
{
    /// <summary>
    /// Calculates the cost of <paramref name="productId"/> using the versions of every input
    /// that were effective on <paramref name="asOf"/>.
    /// </summary>
    /// <param name="productId">Product to calculate.</param>
    /// <param name="asOf">Date the inputs are resolved against; supplied explicitly so past dates stay reproducible.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The breakdown, complete or with warnings describing missing inputs.</returns>
    Task<CostBreakdown> CalculateAsync(Guid productId, DateOnly asOf, CancellationToken ct);
}
