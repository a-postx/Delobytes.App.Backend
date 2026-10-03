using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface IWorkRateRepository
{
    Task<WorkRate?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<WorkRate>> GetAllAsync(CancellationToken ct);

    /// <summary>
    /// Returns the work rate version that was effective on <paramref name="asOf"/>: the latest version
    /// whose ValidFrom does not exceed the date. Deactivation is deliberately ignored so that a rate
    /// superseded later still resolves for an earlier date.
    /// </summary>
    /// <param name="workRateId">Work rate to look up.</param>
    /// <param name="asOf">Date the rate is resolved against.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The effective work rate version, or null when none existed by that date.</returns>
    Task<WorkRate?> GetEffectiveAtAsync(Guid workRateId, DateOnly asOf, CancellationToken ct);

    void Add(WorkRate workRate);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
