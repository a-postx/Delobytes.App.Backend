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
    public async Task MarkPendingAsProcessedAsync(Guid syncJobId, DateTimeOffset processedAt, CancellationToken cancellationToken)
    {
        // Bulk update: goes straight to the database and still honours the tenant query filter
        // configured on RawApiResponse, so it cannot touch rows of another tenant.
        await _context.RawApiResponses
            .Where(r => r.SyncJobId == syncJobId && r.ProcessedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(r => r.ProcessedAt, processedAt),
                cancellationToken);
    }
}

