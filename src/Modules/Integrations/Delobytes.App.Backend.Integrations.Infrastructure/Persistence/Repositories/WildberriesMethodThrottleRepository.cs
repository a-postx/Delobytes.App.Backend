using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of IWildberriesMethodThrottleRepository.
/// </summary>
public class WildberriesMethodThrottleRepository : IWildberriesMethodThrottleRepository
{
    private readonly IntegrationsDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="WildberriesMethodThrottleRepository"/> class.
    /// </summary>
    public WildberriesMethodThrottleRepository(IntegrationsDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public Task<WildberriesMethodThrottle?> GetAsync(
        string keyFingerprint,
        WildberriesApiMethod method,
        CancellationToken cancellationToken)
    {
        return _context.WildberriesMethodThrottles
            .FirstOrDefaultAsync(
                t => t.KeyFingerprint == keyFingerprint && t.Method == method,
                cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(WildberriesMethodThrottle throttle, CancellationToken cancellationToken)
    {
        WildberriesMethodThrottle? existing = await _context.WildberriesMethodThrottles
            .FirstOrDefaultAsync(
                t => t.KeyFingerprint == throttle.KeyFingerprint && t.Method == throttle.Method,
                cancellationToken);

        if (existing is null)
        {
            if (throttle.Id == Guid.Empty)
            {
                throttle.Id = Guid.NewGuid();
            }

            _context.WildberriesMethodThrottles.Add(throttle);
        }
        else
        {
            existing.NextRequestAllowedAt = throttle.NextRequestAllowedAt;
            existing.CurrentIntervalSeconds = throttle.CurrentIntervalSeconds;
            existing.LastLimit = throttle.LastLimit;
            existing.LastRemaining = throttle.LastRemaining;
            existing.LastRetryAfterSeconds = throttle.LastRetryAfterSeconds;
            existing.LastStatusCode = throttle.LastStatusCode;
            existing.ObservedWindowSeconds = throttle.ObservedWindowSeconds;
            existing.SuccessStreak = throttle.SuccessStreak;
            existing.LastRequestAt = throttle.LastRequestAt;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
