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
    {
        return _context.Connections.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public Task<Connection?> FindByIdWithChannelAsync(Guid id, CancellationToken cancellationToken)
    {
        return _context.Connections
                .Include(c => c.Channel)
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public Task<List<Connection>> GetAllByTenantAsync(CancellationToken ct)
    {
        return _context.Connections
                .Include(c => c.Channel)
                .Where(c => c.IsActive)
                .ToListAsync(ct);
    }

    public Task<bool> ExistsForTemplateAsync(string templateCode, CancellationToken ct)
    {
        return _context.Connections
                .Include(c => c.Channel)
                .AnyAsync(c => c.IsActive && c.Channel.Code == templateCode, ct);
    }

    public void Add(Connection connection)
    {
        _context.Connections.Add(connection);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }
}
