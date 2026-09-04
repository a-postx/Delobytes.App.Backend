using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Identity.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of IUserRepository.
/// </summary>
public class UserRepository : IUserRepository
{
    private readonly IdentityDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserRepository"/> class.
    /// </summary>
    public UserRepository(IdentityDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public Task<User?> FindByExternalIdAsync(string externalId, string identityProvider, CancellationToken cancellationToken)
        => _context.Users
            .FirstOrDefaultAsync(
                u => u.ExternalId == externalId && u.IdentityProvider == identityProvider,
                cancellationToken);

    /// <inheritdoc/>
    public Task<User?> FindByEmailAsync(string email, string identityProvider, CancellationToken cancellationToken)
        => _context.Users
            .FirstOrDefaultAsync(
                u => u.Email == email && u.IdentityProvider == identityProvider,
                cancellationToken);

    /// <inheritdoc/>
    public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken)
        => _context.Users
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

    /// <inheritdoc/>
    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
        => _context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    /// <inheritdoc/>
    public void Add(User user)
        => _context.Users.Add(user);

    /// <inheritdoc/>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        => _context.SaveChangesAsync(cancellationToken);
}
