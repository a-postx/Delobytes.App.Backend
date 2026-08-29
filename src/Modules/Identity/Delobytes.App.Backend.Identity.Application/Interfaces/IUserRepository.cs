using Delobytes.App.Backend.Identity.Domain.Entities;

namespace Delobytes.App.Backend.Identity.Application.Interfaces;

/// <summary>
/// Repository interface for User aggregate.
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Finds a user by external provider identifier.
    /// </summary>
    public Task<User?> FindByExternalIdAsync(string externalId, string identityProvider, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a user by email and identity provider.
    /// </summary>
    public Task<User?> FindByEmailAsync(string email, string identityProvider, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a user by primary key.
    /// </summary>
    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a new user.
    /// </summary>
    public void Add(User user);

    /// <summary>
    /// Persists all pending changes.
    /// </summary>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
