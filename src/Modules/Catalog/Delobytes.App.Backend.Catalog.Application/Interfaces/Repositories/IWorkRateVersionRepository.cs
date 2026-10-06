using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface IWorkRateVersionRepository
{
    Task<WorkRateVersion?> GetActiveByWorkRateIdAsync(Guid workRateId, CancellationToken ct);

    Task<WorkRateVersion?> GetLatestByWorkRateIdAsync(Guid workRateId, CancellationToken ct);

    /// <summary>
    /// Returns the wage version that was effective on <paramref name="asOf"/>: the latest version whose
    /// ValidFrom does not exceed the date. Deactivation is deliberately ignored, because a version
    /// replaced later was still the only effective one on an earlier date.
    /// </summary>
    /// <param name="workRateId">Work rate to look up.</param>
    /// <param name="asOf">Date the wage is resolved against.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The effective wage version, or null when the work rate had no wage by that date.</returns>
    Task<WorkRateVersion?> GetEffectiveAtAsync(Guid workRateId, DateOnly asOf, CancellationToken ct);

    /// <summary>
    /// Resolves the active wage version for every requested work rate in a single round trip,
    /// so that listing work rates does not issue one query per row.
    /// </summary>
    /// <param name="workRateIds">Work rates to look up.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Active versions keyed by WorkRateId; work rates with no active version are absent.</returns>
    Task<IReadOnlyDictionary<Guid, WorkRateVersion>> GetActiveByWorkRateIdsAsync(
        IReadOnlyList<Guid> workRateIds, CancellationToken ct);

    void Add(WorkRateVersion version);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
