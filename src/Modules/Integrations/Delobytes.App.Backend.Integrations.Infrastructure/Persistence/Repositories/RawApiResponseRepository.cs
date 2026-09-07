using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of IRawApiResponseRepository.
/// </summary>
public class RawApiResponseRepository : IRawApiResponseRepository
{
    private readonly IntegrationsDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="RawApiResponseRepository"/> class.
    /// </summary>
    /// <param name="context">Database context.</param>
    public RawApiResponseRepository(IntegrationsDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public void Add(RawApiResponse rawApiResponse)
    {
        _context.RawApiResponses.Add(rawApiResponse);
    }

    /// <inheritdoc/>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }
}
