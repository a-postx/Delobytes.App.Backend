using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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

    /// <inheritdoc/>
    public Task<int> DeleteOlderThanAsync(DateTimeOffset threshold, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: the sweep runs on behalf of the whole system, not of the caller's
        // tenant, and must reach every tenant's rows. ExecuteDeleteAsync emits a single DELETE
        // instead of materialising the rows only to mark them deleted.
        return _context.RawApiResponses
            .IgnoreQueryFilters()
            .Where(r => r.ReceivedAt < threshold)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
