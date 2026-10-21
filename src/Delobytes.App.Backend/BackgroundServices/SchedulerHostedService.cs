using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
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
    /// <summary>
    /// SystemChannelTemplate.Code for Wildberries connections, compared case-insensitively.
    /// </summary>
    private const string WildberriesChannelCode = "wildberries";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BackgroundJobsOptions _options;
    private readonly ILogger<SchedulerHostedService> _logger;

    public SchedulerHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<BackgroundJobsOptions> options,
        ILogger<SchedulerHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
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
        DateTimeOffset now = DateTimeOffset.UtcNow;
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
        _logger.LogInformation(
            "SchedulerHostedService: ProcessScheduleAsync ENTRY for schedule {ScheduleId}, connection {ConnectionId}, tenant {TenantId}",
            schedule.Id,
            schedule.ConnectionId,
            schedule.TenantId);

        using IServiceScope scope = _scopeFactory.CreateScope();

        MessageTenantContext tenantContext = scope.ServiceProvider.GetRequiredService<MessageTenantContext>();
        tenantContext.SetTenantId(schedule.TenantId);

        try
        {
            IConnectionRepository connectionRepository = scope.ServiceProvider.GetRequiredService<IConnectionRepository>();
            IJobRunRepository jobRunRepository = scope.ServiceProvider.GetRequiredService<IJobRunRepository>();
            IJobScheduleRepository scheduleRepository = scope.ServiceProvider.GetRequiredService<IJobScheduleRepository>();
            ITenantSettingsProvider tenantSettingsProvider = scope.ServiceProvider.GetRequiredService<ITenantSettingsProvider>();

            _logger.LogInformation(
                "SchedulerHostedService: About to fetch connection {ConnectionId}",
                schedule.ConnectionId);

            Connection? connection = await connectionRepository.FindByIdWithTemplateAsync(schedule.ConnectionId, cancellationToken);

            _logger.LogInformation(
                "SchedulerHostedService: Connection fetched: IsNull={IsNull}, IsActive={IsActive}",
                connection == null,
                connection?.IsActive);

            if (connection == null || !connection.IsActive)
            {
                _logger.LogWarning(
                    "SchedulerHostedService: connection {ConnectionId} for schedule {ScheduleId} is missing or inactive, skipping this tick.",
                    schedule.ConnectionId,
                    schedule.Id);

                await AdvanceScheduleAsync(schedule, now, scheduleRepository, tenantSettingsProvider, cancellationToken);
                return;
            }

            _logger.LogInformation(
                "SchedulerHostedService: About to check HasActiveJobAsync for connection {ConnectionId}, type {JobType}",
                schedule.ConnectionId,
                schedule.JobType);

            bool hasActiveJob = await jobRunRepository.HasActiveJobAsync(schedule.ConnectionId, schedule.JobType, cancellationToken);

            _logger.LogInformation(
                "SchedulerHostedService: HasActiveJob result: {HasActiveJob}",
                hasActiveJob);

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

            if (schedule.JobType == JobType.OrdersImport
                && string.Equals(connection.SystemChannelTemplate?.Code, WildberriesChannelCode, StringComparison.OrdinalIgnoreCase))
            {
                IWildberriesRateLimiter rateLimiter = scope.ServiceProvider.GetRequiredService<IWildberriesRateLimiter>();

                DateTimeOffset nextAllowedAt = await rateLimiter.GetNextRequestAllowedAtAsync(
                    connection.ApiKey, WildberriesApiMethod.SupplierOrders, cancellationToken);

                if (nextAllowedAt > now)
                {
                    // Same semantics as the hasActiveJob skip above: NextRunAt stays untouched so
                    // the schedule is re-evaluated on every subsequent tick until the limiter
                    // slot opens, instead of waiting for the next cron boundary.
                    _logger.LogDebug(
                        "SchedulerHostedService: schedule {ScheduleId} skipped, Wildberries rate " +
                        "limiter slot for connection {ConnectionId} is not available until {NextAllowedAt:O}.",
                        schedule.Id,
                        schedule.ConnectionId,
                        nextAllowedAt);
                    return;
                }
            }

            JobRun jobRun = new JobRun
            {
                Id = Guid.NewGuid(),
                ConnectionId = schedule.ConnectionId,
                JobType = schedule.JobType,
                Status = JobStatus.Pending,
                RequestedByUserId = null,
                CreatedAt = now,
            };

            _logger.LogInformation(
                "SchedulerHostedService: About to create JobRun, current tenantContext.TenantId = {TenantId}",
                tenantContext.TenantId);

            await jobRunRepository.CreateAsync(jobRun, cancellationToken);

            switch (schedule.JobType)
            {
                case JobType.OrdersImport:
                    await jobRunRepository.CreateOrdersImportJobAsync(
                        new OrdersImportJob { JobRunId = jobRun.Id },
                        cancellationToken);
                    break;

                case JobType.ProductsImport:
                    await jobRunRepository.CreateProductsImportJobAsync(
                        new ProductsImportJob { JobRunId = jobRun.Id },
                        cancellationToken);
                    break;

                default:
                    _logger.LogWarning(
                        "SchedulerHostedService: schedule {ScheduleId} has unsupported JobType {JobType}; JobRun {JobRunId} created without type-specific data.",
                        schedule.Id,
                        schedule.JobType,
                        jobRun.Id);
                    break;
            }

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
