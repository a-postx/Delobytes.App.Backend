using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Options;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.BackgroundServices;

/// <summary>
/// Ticks on a fixed interval and deletes every raw API response older than
/// <see cref="RawApiResponseCleanupOptions.RetentionDays"/>, across every tenant. The only bound
/// on the table's growth: JobRuns are never deleted, so the cascade from JobRun never fires.
/// </summary>
public class RawApiResponseCleanupHostedService : BackgroundService
{
    private const int DefaultRetentionDays = 90;
    private const int DefaultIntervalHours = 24;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RawApiResponseCleanupOptions _options;
    private readonly ILogger<RawApiResponseCleanupHostedService> _logger;

    public RawApiResponseCleanupHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<RawApiResponseCleanupOptions> options,
        ILogger<RawApiResponseCleanupHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new PeriodicTimer(GetInterval());

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RawApiResponseCleanupHostedService: tick failed.");
            }
        }
    }

    // PeriodicTimer rejects a non-positive period, and a misconfigured interval must not take
    // the whole host down at startup: fall back to the documented default instead.
    private TimeSpan GetInterval()
    {
        if (_options.IntervalHours <= 0)
        {
            _logger.LogWarning(
                "RawApiResponseCleanupHostedService: invalid interval {IntervalHours} hour(s), falling back to {DefaultIntervalHours}.",
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
                "RawApiResponseCleanupHostedService: invalid retention {RetentionDays} day(s), falling back to {DefaultRetentionDays}.",
                _options.RetentionDays,
                DefaultRetentionDays);

            return DefaultRetentionDays;
        }

        return _options.RetentionDays;
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset threshold = DateTimeOffset.UtcNow.AddDays(-GetRetentionDays());

        using IServiceScope scope = _scopeFactory.CreateScope();

        IRawApiResponseRepository rawApiResponseRepository = scope.ServiceProvider.GetRequiredService<IRawApiResponseRepository>();

        int deletedCount = await rawApiResponseRepository.DeleteOlderThanAsync(threshold, cancellationToken);

        _logger.LogInformation(
            "RawApiResponseCleanupHostedService: deleted {DeletedCount} raw API response(s) received before {Threshold:O}.",
            deletedCount,
            threshold);
    }
}
