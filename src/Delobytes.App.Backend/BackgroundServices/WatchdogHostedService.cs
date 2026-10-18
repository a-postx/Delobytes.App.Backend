using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence;
using Delobytes.App.Backend.Services;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.BackgroundServices;

/// <summary>
/// Ticks on a fixed interval and transitions every Running <see cref="JobRun"/> that has
/// exceeded its per-type timeout (<see cref="JobTypeConfiguration.TimeoutMinutes"/>) to Failed.
/// Terminal: no automatic retry here, a fresh run is created either by the next schedule tick
/// (SchedulerHostedService) or by a manual restart.
/// </summary>
public class WatchdogHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WatchdogOptions _options;
    private readonly ILogger<WatchdogHostedService> _logger;

    public WatchdogHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<WatchdogOptions> options,
        ILogger<WatchdogHostedService> logger)
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
                _logger.LogError(ex, "WatchdogHostedService: tick failed.");
            }
        }
    }

    // PeriodicTimer rejects a non-positive period, and a misconfigured interval must not take
    // the whole host down at startup: fall back to the documented default instead.
    private TimeSpan GetInterval()
    {
        if (_options.IntervalMinutes <= 0)
        {
            _logger.LogWarning(
                "WatchdogHostedService: invalid interval {IntervalMinutes} minute(s), falling back to 5.",
                _options.IntervalMinutes);

            return TimeSpan.FromMinutes(5);
        }

        return TimeSpan.FromMinutes(_options.IntervalMinutes);
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        List<StuckJobCandidate> stuckJobs = await CollectStuckJobsAsync(cancellationToken);

        if (stuckJobs.Count == 0)
        {
            return;
        }

        _logger.LogInformation("WatchdogHostedService: {Count} job run(s) exceeded their timeout.", stuckJobs.Count);

        foreach (StuckJobCandidate stuckJob in stuckJobs)
        {
            try
            {
                await FailJobAsync(stuckJob, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WatchdogHostedService: failed to time out JobRun {JobRunId}.", stuckJob.JobRunId);
            }
        }
    }

    // One scope for the whole lookup: JobTypeConfiguration is a system table shared by every
    // tenant, and IJobRunRepository.GetRunningJobsOlderThanAsync already spans every tenant
    // via IgnoreQueryFilters. TenantId is read here (while the entities are still tracked by
    // this scope's DbContext) because JobRun exposes it only as a shadow property.
    private async Task<List<StuckJobCandidate>> CollectStuckJobsAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();

        IJobRunRepository jobRunRepository = scope.ServiceProvider.GetRequiredService<IJobRunRepository>();
        IJobTypeConfigurationRepository jobTypeConfigurationRepository = scope.ServiceProvider.GetRequiredService<IJobTypeConfigurationRepository>();
        IntegrationsDbContext context = scope.ServiceProvider.GetRequiredService<IntegrationsDbContext>();

        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<StuckJobCandidate> candidates = new List<StuckJobCandidate>();

        foreach (JobType jobType in Enum.GetValues<JobType>())
        {
            JobTypeConfiguration? configuration = await jobTypeConfigurationRepository.GetByTypeAsync(jobType, cancellationToken);

            if (configuration == null)
            {
                // Not every JobType necessarily has a configured timeout (e.g. OrdersSync is
                // reserved for a future task type); nothing to time out for it yet.
                continue;
            }

            DateTimeOffset threshold = now.AddMinutes(-configuration.TimeoutMinutes);
            List<JobRun> runningOlderThanThreshold = await jobRunRepository.GetRunningJobsOlderThanAsync(threshold, cancellationToken);

            // GetRunningJobsOlderThanAsync is not JobType-aware: it returns every Running job
            // older than the threshold passed in, regardless of type. Filtering here avoids
            // timing out a different job type against a threshold derived from this type's
            // (possibly shorter) timeout.
            foreach (JobRun jobRun in runningOlderThanThreshold.Where(j => j.JobType == jobType))
            {
                Guid tenantId = (Guid)context.Entry(jobRun).Property("TenantId").CurrentValue!;
                candidates.Add(new StuckJobCandidate(jobRun.Id, tenantId, configuration.TimeoutMinutes));
            }
        }

        return candidates;
    }

    // One scope per job: the stuck job's tenant must be set as the ambient tenant before any
    // tenant-scoped repository resolved inside can see (or persist changes to) the row.
    private async Task FailJobAsync(StuckJobCandidate stuckJob, CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();

        MessageTenantContext tenantContext = scope.ServiceProvider.GetRequiredService<MessageTenantContext>();
        tenantContext.SetTenantId(stuckJob.TenantId);

        try
        {
            IJobRunRepository jobRunRepository = scope.ServiceProvider.GetRequiredService<IJobRunRepository>();

            JobRun? jobRun = await jobRunRepository.GetByIdAsync(stuckJob.JobRunId, cancellationToken);

            if (jobRun == null || jobRun.Status != JobStatus.Running)
            {
                // Completed, cancelled or already timed out concurrently between the lookup
                // scope above and this scope.
                return;
            }

            jobRun.Status = JobStatus.Failed;
            jobRun.Error = $"Job exceeded timeout of {stuckJob.TimeoutMinutes} minutes";
            jobRun.CompletedAt = DateTimeOffset.UtcNow;

            await jobRunRepository.UpdateAsync(jobRun, cancellationToken);

            _logger.LogWarning(
                "WatchdogHostedService: JobRun {JobRunId} ({JobType}, tenant {TenantId}) timed out after {TimeoutMinutes} minute(s).",
                jobRun.Id,
                jobRun.JobType,
                stuckJob.TenantId,
                stuckJob.TimeoutMinutes);
        }
        finally
        {
            tenantContext.Clear();
        }
    }

    private sealed record StuckJobCandidate(Guid JobRunId, Guid TenantId, int TimeoutMinutes);
}
