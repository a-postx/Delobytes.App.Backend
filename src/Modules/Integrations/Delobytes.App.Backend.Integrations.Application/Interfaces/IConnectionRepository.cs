using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Repository interface for Connection aggregate.
/// </summary>
public interface IConnectionRepository
{
    /// <summary>
    /// Finds a connection by primary key.
    /// </summary>
    /// <param name="id">Connection identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Connection entity or null.</returns>
    Task<Connection?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a connection by primary key with related Channel loaded.
    /// </summary>
    /// <param name="id">Connection identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Connection entity with Channel or null.</returns>
    Task<Connection?> FindByIdWithChannelAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Persists all pending changes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of state entries written to the database.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
