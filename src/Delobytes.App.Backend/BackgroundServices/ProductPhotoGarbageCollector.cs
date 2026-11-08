using Delobytes.App.Backend.Catalog.Application.Options;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Catalog.Infrastructure.Storage;
using Delobytes.App.Backend.Contracts.Storage;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.BackgroundServices;

/// <summary>
/// Ticks on a fixed interval and deletes every photo row that has stayed in
/// <see cref="ProductPhotoStatus.Removed"/> longer than
/// <see cref="ProductPhotoRetentionOptions.RetentionDays"/>, along with its blob.
/// </summary>
public class ProductPhotoGarbageCollector : BackgroundService
{
    private const int DefaultRetentionDays = 30;
    private const int DefaultIntervalHours = 24;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ProductPhotoRetentionOptions _options;
    private readonly ILogger<ProductPhotoGarbageCollector> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductPhotoGarbageCollector"/> class.
    /// </summary>
    /// <param name="scopeFactory">Factory for the per-tenant scope that owns the DbContext.</param>
    /// <param name="options">Retention policy.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="timeProvider">Clock used to derive the retention cutoff.</param>
    public ProductPhotoGarbageCollector(
        IServiceScopeFactory scopeFactory,
        IOptions<ProductPhotoRetentionOptions> options,
        ILogger<ProductPhotoGarbageCollector> logger,
        TimeProvider timeProvider)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new PeriodicTimer(GetInterval());

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                ProductPhotoGcResult result = await CleanupAsync(_timeProvider.GetUtcNow(), stoppingToken);

                _logger.LogInformation(
                    "ProductPhotoGarbageCollector: deleted {DeletedPhotos} photo row(s) and {DeletedBlobs} blob(s); {FailedTenants} tenant(s) failed.",
                    result.DeletedPhotos,
                    result.DeletedBlobs,
                    result.FailedTenants);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ProductPhotoGarbageCollector: tick failed.");
            }
        }
    }

    /// <summary>
    /// Deletes every expired <c>Removed</c> photo row, and the blobs that has no live row references.
    /// </summary>
    /// <param name="now">Current time; the retention cutoff is derived from it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Per-run counters.</returns>
    public async Task<ProductPhotoGcResult> CleanupAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        ProductPhotoGcResult result = new ProductPhotoGcResult();
        DateTimeOffset threshold = now.AddDays(-GetRetentionDays());

        List<Guid> tenantIds;

        using (IServiceScope lookupScope = _scopeFactory.CreateScope())
        {
            ITenantRepository tenantRepository = lookupScope.ServiceProvider.GetRequiredService<ITenantRepository>();
            tenantIds = await tenantRepository.GetAllActiveTenantIdsAsync(cancellationToken);
        }

        foreach (Guid tenantId in tenantIds)
        {
            try
            {
                (int deletedPhotos, int deletedBlobs) = await CleanupTenantAsync(tenantId, threshold, cancellationToken);

                result.DeletedPhotos += deletedPhotos;
                result.DeletedBlobs += deletedBlobs;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.FailedTenants++;

                // The failed tenant's scope is disposed with its dirty change tracker, so nothing
                // of it can leak into the remaining tenants; its rows are retried on the next tick.
                _logger.LogError(
                    ex,
                    "ProductPhotoGarbageCollector: cleanup failed for TenantId {TenantId}; its rows are retried on the next tick.",
                    tenantId);
            }
        }

        return result;
    }

    private async Task<(int DeletedPhotos, int DeletedBlobs)> CleanupTenantAsync(
        Guid tenantId,
        DateTimeOffset threshold,
        CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();

        MessageTenantContext tenantContext = scope.ServiceProvider.GetRequiredService<MessageTenantContext>();
        tenantContext.SetTenantId(tenantId);

        try
        {
            CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            IObjectStorage objectStorage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();

            List<ProductPhoto> expiredPhotos = await context.ProductPhotos
                .Where(p => p.Status == ProductPhotoStatus.Removed
                            && p.RemovedAt != null
                            && p.RemovedAt < threshold)
                .ToListAsync(cancellationToken);

            if (expiredPhotos.Count == 0)
            {
                return (0, 0);
            }

            HashSet<string> candidateKeys = expiredPhotos
                .Select(p => p.StorageKey)
                .Where(k => !string.IsNullOrEmpty(k))
                .ToHashSet(StringComparer.Ordinal);

            HashSet<string> liveKeys = await GetLiveKeysAsync(context, candidateKeys, cancellationToken);

            // Identical bytes on two channels of one product resolve to one key, so a key may only go
            // when no live row still points at it; deleting on the first Removed row would blank a live photo.
            List<string> deletableKeys = candidateKeys
                .Where(k => !liveKeys.Contains(k))
                .ToList();

            if (deletableKeys.Count > 0)
            {
                await objectStorage.DeleteManyAsync(StorageArea.ProductPhotos, deletableKeys, cancellationToken);
            }

            context.ProductPhotos.RemoveRange(expiredPhotos);

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogDebug(
                "ProductPhotoGarbageCollector: TenantId {TenantId}: {DeletedPhotos} row(s) deleted, {DeletedBlobs} blob(s) deleted, {SharedBlobs} blob(s) kept for live rows.",
                tenantId,
                expiredPhotos.Count,
                deletableKeys.Count,
                candidateKeys.Count - deletableKeys.Count);

            return (expiredPhotos.Count, deletableKeys.Count);
        }
        finally
        {
            tenantContext.Clear();
        }
    }

    // Keys are content-addressed, so two rows of one tenant share a key exactly when they share
    // bytes; the (TenantId, ProductId, ContentHash) index backs this lookup. The tenant filter is
    // what scopes the check here, no filter bypass is needed.
    private static async Task<HashSet<string>> GetLiveKeysAsync(
        CatalogDbContext context,
        HashSet<string> candidateKeys,
        CancellationToken cancellationToken)
    {
        if (candidateKeys.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        List<string> liveKeys = await context.ProductPhotos
            .Where(p => p.Status != ProductPhotoStatus.Removed
                        && candidateKeys.Contains(p.StorageKey))
            .Select(p => p.StorageKey)
            .ToListAsync(cancellationToken);

        return liveKeys.ToHashSet(StringComparer.Ordinal);
    }

    // PeriodicTimer rejects a non-positive period, and a misconfigured interval must not take the
    // whole host down at startup: fall back to the documented default instead.
    private TimeSpan GetInterval()
    {
        if (_options.IntervalHours <= 0)
        {
            _logger.LogWarning(
                "ProductPhotoGarbageCollector: invalid interval {IntervalHours} hour(s), falling back to {DefaultIntervalHours}.",
                _options.IntervalHours,
                DefaultIntervalHours);

            return TimeSpan.FromHours(DefaultIntervalHours);
        }

        return TimeSpan.FromHours(_options.IntervalHours);
    }

    private int GetRetentionDays()
    {
        if (_options.RetentionDays <= 0)
        {
            _logger.LogWarning(
                "ProductPhotoGarbageCollector: invalid retention {RetentionDays} day(s), falling back to {DefaultRetentionDays}.",
                _options.RetentionDays,
                DefaultRetentionDays);

            return DefaultRetentionDays;
        }

        return _options.RetentionDays;
    }
}
