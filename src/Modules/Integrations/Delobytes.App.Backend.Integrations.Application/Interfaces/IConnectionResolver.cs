using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Resolves the active Connection for a given channel code within the current tenant scope.
/// </summary>
public interface IConnectionResolver
{
    public Task<Connection> ResolveAsync(string channelCode, CancellationToken ct);

    /// <summary>
    /// Resolves an active Connection by its identifier, regardless of channel code.
    /// Used instead of <see cref="ResolveAsync"/> wherever the specific connection to
    /// authenticate against is already known (e.g. a JobRun), so that tenants with multiple
    /// connections for the same channel are not resolved arbitrarily.
    /// </summary>
    public Task<Connection> ResolveByIdAsync(Guid connectionId, CancellationToken ct);
}
