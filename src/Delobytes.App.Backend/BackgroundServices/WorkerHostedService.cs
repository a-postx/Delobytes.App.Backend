using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Events;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Services;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.BackgroundServices;

/// <summary>
/// Ticks on a fixed interval and, for every active tenant, dispatches as many Pending
/// <see cref="JobRun"/> rows as fit within the tenant's configured background job
/// parallelism (Tenant.MaxBackgroundJobsParallelism). Dispatch means publishing the internal
/// event that triggers the corresponding orchestration consumer (ProcessProductsImportConsumer,
/// ProcessOrdersImportConsumer); the consumer itself transitions the JobRun to Running.
/// </summary>
public class WorkerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BackgroundJobsOptions _options;
    private readonly ILogger<WorkerHostedService> _logger;

    public WorkerHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<BackgroundJobsOptions> options,
        ILogger<WorkerHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.WorkerTickIntervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WorkerHostedService: tick failed.");
            }
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
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
                await ProcessTenantAsync(tenantId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WorkerHostedService: failed to dispatch pending jobs for tenant {TenantId}.", tenantId);
            }
        }
    }

    // One scope per tenant: a scoped DbContext resolved here must see exactly this tenant's
    // ambient TenantId, both for the query filter and for the parallelism accounting below.
    private async Task ProcessTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();

        MessageTenantContext tenantContext = scope.ServiceProvider.GetRequiredService<MessageTenantContext>();
        tenantContext.SetTenantId(tenantId);

        try
        {
            IJobRunRepository jobRunRepository = scope.ServiceProvider.GetRequiredService<IJobRunRepository>();
            ITenantSettingsProvider tenantSettingsProvider = scope.ServiceProvider.GetRequiredService<ITenantSettingsProvider>();
            IEventPublisher eventPublisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

            int parallelism = await tenantSettingsProvider.GetMaxBackgroundJobsParallelismAsync(tenantId, cancellationToken);

            if (parallelism <= 0)
            {
                return;
            }

            int runningCount = await jobRunRepository.CountRunningJobsAsync(tenantId, cancellationToken);
            int availableSlots = parallelism - runningCount;

            if (availableSlots <= 0)
            {
                return;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;

            List<JobRun> pendingJobs = await jobRunRepository.GetPendingJobsAsync(tenantId, availableSlots, now, cancellationToken);

            foreach (JobRun jobRun in pendingJobs)
            {
                await DispatchAsync(jobRun, eventPublisher, cancellationToken);
            }
        }
        finally
        {
            tenantContext.Clear();
        }
    }

    private async Task DispatchAsync(JobRun jobRun, IEventPublisher eventPublisher, CancellationToken cancellationToken)
    {
        switch (jobRun.JobType)
        {
            case JobType.ProductsImport:
                await eventPublisher.PublishAsync(
                    new ProductsImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = jobRun.ConnectionId },
                    cancellationToken);
                break;

            case JobType.OrdersImport:
                await eventPublisher.PublishAsync(
                    new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = jobRun.ConnectionId },
                    cancellationToken);
                break;

            default:
                _logger.LogWarning(
                    "WorkerHostedService: JobRun {JobRunId} has unsupported JobType {JobType}, cannot dispatch.",
                    jobRun.Id,
                    jobRun.JobType);
                return;
        }

        _logger.LogInformation(
            "WorkerHostedService: dispatched JobRun {JobRunId} ({JobType}) for connection {ConnectionId}.",
            jobRun.Id,
            jobRun.JobType,
            jobRun.ConnectionId);
    }
}
