using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Resolves the active Connection for a given channel code within the current tenant scope.
/// </summary>
public interface IConnectionResolver
{
    public Task<Connection> ResolveAsync(string channelCode, CancellationToken ct);
}
