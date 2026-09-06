using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of IConnectionRepository.
/// </summary>
public class ConnectionRepository : IConnectionRepository
{
    private readonly IntegrationsDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionRepository"/> class.
    /// </summary>
    /// <param name="context">Database context.</param>
    public ConnectionRepository(IntegrationsDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public Task<Connection?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
        => _context.Connections.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    /// <inheritdoc/>
    public Task<Connection?> FindByIdWithChannelAsync(Guid id, CancellationToken cancellationToken)
        => _context.Connections
            .Include(c => c.Channel)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    /// <inheritdoc/>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        => _context.SaveChangesAsync(cancellationToken);
}
