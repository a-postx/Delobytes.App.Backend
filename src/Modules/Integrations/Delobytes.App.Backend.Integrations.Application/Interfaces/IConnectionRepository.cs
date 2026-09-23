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
    public Task<Connection?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a connection by primary key with related SystemChannelTemplate loaded.
    /// </summary>
    /// <param name="id">Connection identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Connection entity with Template or null.</returns>
    public Task<Connection?> FindByIdWithTemplateAsync(Guid id, CancellationToken cancellationToken);

    public Task<List<Connection>> GetAllByTenantAsync(CancellationToken ct);

    /// <summary>
    /// Checks whether the given Catalog Channel already has an active connection.
    /// Used to enforce "one active connection per channel" without knowledge of the Channel entity itself.
    /// </summary>
    public Task<bool> HasActiveConnectionForChannelAsync(Guid channelId, CancellationToken ct);

    public void Add(Connection connection);

    /// <summary>
    /// Persists all pending changes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of state entries written to the database.</returns>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
