using System.Security.Cryptography;
using System.Text;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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

    /// <inheritdoc/>
    public async Task<TResult> MutateAsync<TResult>(
        string keyFingerprint,
        WildberriesApiMethod method,
        Func<WildberriesMethodThrottle> createDefault,
        Func<WildberriesMethodThrottle, TResult> mutate,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await _context.Database.BeginTransactionAsync(cancellationToken);

        // Session-level advisory lock scoped to this (key, method) pair, released automatically
        // at commit/rollback. A plain row lock (SELECT ... FOR UPDATE) cannot protect a row that
        // does not exist yet, which is exactly the case on the very first call for a fresh key;
        // the advisory lock is keyed on the business pair itself, so it serializes concurrent
        // callers even before any row is inserted.
        long lockKey = ComputeAdvisoryLockKey(keyFingerprint, method);
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);

        WildberriesMethodThrottle? throttle = await _context.WildberriesMethodThrottles
            .FirstOrDefaultAsync(
                t => t.KeyFingerprint == keyFingerprint && t.Method == method,
                cancellationToken);

        if (throttle is null)
        {
            throttle = createDefault();
            throttle.KeyFingerprint = keyFingerprint;
            throttle.Method = method;

            if (throttle.Id == Guid.Empty)
            {
                throttle.Id = Guid.NewGuid();
            }

            _context.WildberriesMethodThrottles.Add(throttle);
        }

        TResult result = mutate(throttle);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return result;
    }

    private static long ComputeAdvisoryLockKey(string keyFingerprint, WildberriesApiMethod method)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{keyFingerprint}:{(int)method}"));
        return BitConverter.ToInt64(hash, 0);
    }
}
