using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of ISyncJobRepository.
/// </summary>
public class SyncJobRepository : ISyncJobRepository
{
    private readonly IntegrationsDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncJobRepository"/> class.
    /// </summary>
    public SyncJobRepository(IntegrationsDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public Task<SyncJob?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return _context.SyncJobs.FirstOrDefaultAsync(sj => sj.Id == id, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<SyncJob?> FindByIdWithConnectionAsync(Guid id, CancellationToken cancellationToken)
    {
        return _context.SyncJobs
                .Include(sj => sj.Connection)
                .ThenInclude(c => c.SystemChannelTemplate)
                .FirstOrDefaultAsync(sj => sj.Id == id, cancellationToken);
    }

    /// <inheritdoc/>
    public void Add(SyncJob syncJob)
    {
        _context.SyncJobs.Add(syncJob);
    }

    /// <inheritdoc/>
    public void Update(SyncJob syncJob)
    {
        _context.SyncJobs.Update(syncJob);
    }

    /// <inheritdoc/>
    public Task<List<SyncJob>> GetProductsImportJobsAsync(CancellationToken cancellationToken)
    {
        return _context.SyncJobs
            .Where(j => j.JobType == JobType.ProductsImport)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task<bool> HasActivePendingOrRunningJobForConnectionAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        return _context.SyncJobs.AnyAsync(
            j => j.ConnectionId == connectionId
                && j.JobType == JobType.ProductsImport
                && (j.Status == SyncJobStatus.Pending || j.Status == SyncJobStatus.Running),
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task<bool> BatchResultExistsAsync(Guid messageId, CancellationToken cancellationToken)
    {
        return _context.SyncJobBatchResults.AnyAsync(r => r.MessageId == messageId, cancellationToken);
    }

    /// <inheritdoc/>
    public void AddBatchResult(SyncJobBatchResult batchResult)
    {
        _context.SyncJobBatchResults.Add(batchResult);
    }

    /// <inheritdoc/>
    public async Task<SyncJobBatchResultTotals> GetBatchResultTotalsAsync(Guid syncJobId, CancellationToken cancellationToken)
    {
        List<string?> errors = await _context.SyncJobBatchResults
            .Where(r => r.SyncJobId == syncJobId && r.ErrorMessage != null)
            .Select(r => r.ErrorMessage)
            .ToListAsync(cancellationToken);

        // Aggregate in a single query to avoid N+1.
        SyncJobBatchResultTotals totals = await _context.SyncJobBatchResults
            .Where(r => r.SyncJobId == syncJobId)
            .GroupBy(r => r.SyncJobId)
            .Select(g => new SyncJobBatchResultTotals
            {
                TotalProcessed = g.Sum(r => r.RecordsProcessed),
                TotalCreated = g.Sum(r => r.RecordsCreated),
                TotalUpdated = g.Sum(r => r.RecordsUpdated),
                TotalSkipped = g.Sum(r => r.RecordsSkipped),
                TotalFailed = g.Sum(r => r.RecordsFailed),
                CombinedErrors = null,
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? new SyncJobBatchResultTotals();

        List<string> nonNullErrors = errors
            .Where(e => e != null)
            .Select(e => e!)
            .ToList();

        // Truncate combined errors to 2000 chars to match DB column constraint.
        string? combinedErrors = nonNullErrors.Count > 0
            ? TruncateString(string.Join("; ", nonNullErrors), 2000)
            : null;

        return new SyncJobBatchResultTotals
        {
            TotalProcessed = totals.TotalProcessed,
            TotalCreated = totals.TotalCreated,
            TotalUpdated = totals.TotalUpdated,
            TotalSkipped = totals.TotalSkipped,
            TotalFailed = totals.TotalFailed,
            CombinedErrors = combinedErrors,
        };
    }

    /// <inheritdoc/>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }

    private static string TruncateString(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value.Substring(0, maxLength);
    }
}

