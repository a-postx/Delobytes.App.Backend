using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Integrations.Application.SalesChannels;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Jobs;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Services;
using Microsoft.Extensions.Options;
using NCrontab;

namespace Delobytes.App.Backend.BackgroundServices;

/// <summary>
/// Ticks on a fixed interval and, for every due <see cref="JobSchedule"/> across every tenant,
/// creates a Pending <see cref="JobRun"/> (plus its type-specific row) and advances the
/// schedule's NextRunAt. Does not publish any message itself: dispatch (and therefore the
/// per-tenant parallelism limit) is enforced exclusively by <see cref="WorkerHostedService"/>.
/// </summary>
public class SchedulerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BackgroundJobsOptions _options;
    private readonly ILogger<SchedulerHostedService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IChannelJobTypeRegistry _jobTypes;

    public SchedulerHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<BackgroundJobsOptions> options,
        ILogger<SchedulerHostedService> logger,
        TimeProvider timeProvider,
        IChannelJobTypeRegistry jobTypes)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider;
        _jobTypes = jobTypes;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.SchedulerTickIntervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SchedulerHostedService: tick failed.");
            }
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        List<JobSchedule> dueSchedules;

        using (IServiceScope lookupScope = _scopeFactory.CreateScope())
        {
            IJobScheduleRepository scheduleRepository = lookupScope.ServiceProvider.GetRequiredService<IJobScheduleRepository>();
            dueSchedules = await scheduleRepository.GetDueSchedulesAsync(now, cancellationToken);
        }

        if (dueSchedules.Count == 0)
        {
            return;
        }

        _logger.LogInformation("SchedulerHostedService: {Count} schedule(s) due.", dueSchedules.Count);

        foreach (JobSchedule schedule in dueSchedules)
        {
            try
            {
                await ProcessScheduleAsync(schedule, now, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "SchedulerHostedService: failed to process schedule {ScheduleId} (tenant {TenantId}, connection {ConnectionId}).",
                    schedule.Id,
                    schedule.TenantId,
                    schedule.ConnectionId);
            }
        }
    }

    // One scope per schedule: schedules in the same tick can belong to different tenants, and
    // every tenant-scoped repository resolved inside must see the matching ambient TenantId.
    private async Task ProcessScheduleAsync(JobSchedule schedule, DateTimeOffset now, CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();

        MessageTenantContext tenantContext = scope.ServiceProvider.GetRequiredService<MessageTenantContext>();
        tenantContext.SetTenantId(schedule.TenantId);

        try
        {
            IConnectionRepository connectionRepository = scope.ServiceProvider.GetRequiredService<IConnectionRepository>();
            IJobRunRepository jobRunRepository = scope.ServiceProvider.GetRequiredService<IJobRunRepository>();
            IJobScheduleRepository scheduleRepository = scope.ServiceProvider.GetRequiredService<IJobScheduleRepository>();
            ITenantSettingsProvider tenantSettingsProvider = scope.ServiceProvider.GetRequiredService<ITenantSettingsProvider>();
            JobRunStarter jobRunStarter = scope.ServiceProvider.GetRequiredService<JobRunStarter>();

            Connection? connection = await connectionRepository.FindByIdWithTemplateAsync(schedule.ConnectionId, cancellationToken);

            if (connection == null || !connection.IsActive)
            {
                _logger.LogWarning(
                    "SchedulerHostedService: connection {ConnectionId} for schedule {ScheduleId} is missing or inactive, skipping this tick.",
                    schedule.ConnectionId,
                    schedule.Id);

                await AdvanceScheduleAsync(schedule, now, scheduleRepository, tenantSettingsProvider, cancellationToken);
                return;
            }

            string? channelCode = connection.SystemChannelTemplate?.Code;

            // A schedule left over from a previous channel mapping (e.g. an OrdersImport schedule
            // on an Ozon connection) would make every tick create a job the consumer cannot run.
            if (_jobTypes.ResolveKind(schedule.JobType) == ChannelJobKind.OrdersImport
                && !_jobTypes.IsServedByChannel(channelCode, schedule.JobType))
            {
                // NextRunAt is deliberately not advanced: the schedule stays visible as due, so an
                // operator (or the seeder, which disables it) sees that it is wrong.
                _logger.LogDebug(
                    "SchedulerHostedService: schedule {ScheduleId} skipped, job type {JobType} does not " +
                    "belong to connection {ConnectionId} of channel '{ChannelCode}'.",
                    schedule.Id,
                    schedule.JobType,
                    schedule.ConnectionId,
                    channelCode);
                return;
            }

            // Tenant-wide parallelism is enforced here (not only at dispatch time): creating a
            // Pending JobRun while the tenant's slot is already full would just sit in the table
            // until WorkerHostedService's own check lets it through, masking why the schedule
            // "did not fire" from an operator's point of view.
            int parallelism = await tenantSettingsProvider.GetMaxBackgroundJobsParallelismAsync(schedule.TenantId, cancellationToken);
            int runningCount = await jobRunRepository.CountRunningJobsAsync(schedule.TenantId, cancellationToken);

            if (runningCount >= parallelism)
            {
                _logger.LogDebug(
                    "SchedulerHostedService: schedule {ScheduleId} skipped, tenant {TenantId} parallelism " +
                    "limit ({Parallelism}) reached, {RunningCount} job(s) already running.",
                    schedule.Id,
                    schedule.TenantId,
                    parallelism,
                    runningCount);
                return;
            }

            bool hasActiveJob;

            if (SerializesOrdersJobTypes(channelCode))
            {
                // FBO and FBS share one connection and must never run concurrently: they fetch
                // overlapping order_number ranges and write to the same Order row.
                hasActiveJob = await jobRunRepository.HasActiveOzonOrdersJobAsync(schedule.ConnectionId, cancellationToken);

                if (hasActiveJob)
                {
                    _logger.LogDebug(
                        "SchedulerHostedService: schedule {ScheduleId} (JobType {JobType}) skipped, another Ozon orders job " +
                        "is already active on connection {ConnectionId} (sequential execution enforced).",
                        schedule.Id,
                        schedule.JobType,
                        schedule.ConnectionId);
                    return;
                }
            }
            else
            {
                hasActiveJob = await jobRunRepository.HasActiveJobAsync(schedule.ConnectionId, schedule.JobType, cancellationToken);

                if (hasActiveJob)
                {
                    // NextRunAt is deliberately left untouched: the schedule stays due and is
                    // re-evaluated on every subsequent tick until the in-flight job frees the slot.
                    _logger.LogDebug(
                        "SchedulerHostedService: schedule {ScheduleId} skipped, an active job already exists for connection {ConnectionId}.",
                        schedule.Id,
                        schedule.ConnectionId);
                    return;
                }
            }

            bool hasSlot = await HasRateLimitSlotAsync(scope, schedule, connection, channelCode, now, cancellationToken);

            if (!hasSlot)
            {
                return;
            }

            JobRun jobRun = await jobRunStarter.CreatePendingAsync(
                schedule.ConnectionId,
                schedule.JobType,
                requestedByUserId: null,
                cancellationToken);

            _logger.LogInformation(
                "SchedulerHostedService: created JobRun {JobRunId} ({JobType}) for connection {ConnectionId} from schedule {ScheduleId}.",
                jobRun.Id,
                schedule.JobType,
                schedule.ConnectionId,
                schedule.Id);

            await AdvanceScheduleAsync(schedule, now, scheduleRepository, tenantSettingsProvider, cancellationToken);
        }
        finally
        {
            tenantContext.Clear();
        }
    }

    /// <summary>
    /// True when the schedule belongs to an Ozon connection and drives either of the two orders
    /// job types (FBO or FBS) that must never run concurrently on the same connection.
    /// </summary>
    private static bool SerializesOrdersJobTypes(string? channelCode)
    {
        return ChannelCatalog.TryGet(channelCode, out ChannelDescriptor descriptor)
            && descriptor.SerializesOrdersJobTypes;
    }

    /// <summary>
    /// Pre-check that keeps an obviously throttled job from being created at all: the worker would
    /// dispatch it, the fetcher would hit the limiter on its first request and the run would fail
    /// or stop immediately. Returns <c>false</c> when the tick must be skipped, leaving NextRunAt
    /// untouched so the schedule is retried on the next tick.
    /// </summary>
    private async Task<bool> HasRateLimitSlotAsync(
        IServiceScope scope,
        JobSchedule schedule,
        Connection connection,
        string? channelCode,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        string? method = _jobTypes.GetRateLimitMethod(channelCode, schedule.JobType);

        if (method == null
            || !ChannelCatalog.TryGet(channelCode, out ChannelDescriptor descriptor))
        {
            return true;
        }

        // Limiters are keyed by the catalogue's canonical code, which is also what keys their
        // buckets: the template's stored code is not guaranteed to match its casing.
        IChannelRateLimiter rateLimiter = scope.ServiceProvider
            .GetRequiredKeyedService<IChannelRateLimiter>(descriptor.Code);

        RateLimitScope rateLimitScope = new RateLimitScope(
            descriptor.Code,
            connection.ApiKey,
            method);

        DateTimeOffset? nextAllowedAt = await rateLimiter.GetNextRequestAllowedAtAsync(rateLimitScope, cancellationToken);

        if (nextAllowedAt is not null && nextAllowedAt > now)
        {
            // Ждём, не пишем в базу каждый тик, когда очевидно что джоба будет завалена лимитером
            _logger.LogDebug(
                "SchedulerHostedService: schedule {ScheduleId} skipped, rate limiter slot for channel " +
                "'{ChannelCode}' and connection {ConnectionId} is not available until {NextAllowedAt:O}.",
                schedule.Id,
                channelCode,
                schedule.ConnectionId,
                nextAllowedAt);
            return false;
        }

        return true;
    }

    private async Task AdvanceScheduleAsync(
        JobSchedule schedule,
        DateTimeOffset now,
        IJobScheduleRepository scheduleRepository,
        ITenantSettingsProvider tenantSettingsProvider,
        CancellationToken cancellationToken)
    {
        TimeZoneInfo timeZone = await ResolveTenantTimeZoneAsync(tenantSettingsProvider, schedule.TenantId, cancellationToken);

        schedule.LastRunAt = now;
        schedule.NextRunAt = ComputeNextRunAt(schedule.CronExpression, timeZone, now);

        await scheduleRepository.UpdateAsync(schedule, cancellationToken);
    }

    private async Task<TimeZoneInfo> ResolveTenantTimeZoneAsync(
        ITenantSettingsProvider tenantSettingsProvider,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        try
        {
            TenantAccountingSettings settings = await tenantSettingsProvider.GetAccountingSettingsAsync(tenantId, cancellationToken);
            return TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            _logger.LogWarning(
                ex,
                "SchedulerHostedService: could not resolve time zone for tenant {TenantId}, falling back to UTC.",
                tenantId);
            return TimeZoneInfo.Utc;
        }
    }

    private static DateTimeOffset ComputeNextRunAt(string cronExpression, TimeZoneInfo timeZone, DateTimeOffset fromUtc)
    {
        CrontabSchedule parsedSchedule = CrontabSchedule.Parse(cronExpression);
        DateTime fromLocal = TimeZoneInfo.ConvertTime(fromUtc, timeZone).DateTime;
        DateTime nextLocal = parsedSchedule.GetNextOccurrence(fromLocal);
        DateTime nextLocalUnspecified = DateTime.SpecifyKind(nextLocal, DateTimeKind.Unspecified);
        DateTime nextUtc = TimeZoneInfo.ConvertTimeToUtc(nextLocalUnspecified, timeZone);
        return new DateTimeOffset(nextUtc, TimeSpan.Zero);
    }
}
