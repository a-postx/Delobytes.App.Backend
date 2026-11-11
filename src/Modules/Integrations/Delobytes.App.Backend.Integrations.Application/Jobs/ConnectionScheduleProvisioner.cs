using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Extensions;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Integrations.Application.Jobs;

/// <summary>
/// Creates the orders <see cref="JobSchedule"/> rows for a connection created while the application
/// is running, so adding a connection schedules its import immediately instead of at the next
/// restart.
/// </summary>
public class ConnectionScheduleProvisioner
{
    private readonly IConnectionRepository _connectionRepository;
    private readonly IJobScheduleRepository _scheduleRepository;
    private readonly ITenantContext _tenantContext;
    private readonly BackgroundJobsOptions _backgroundJobsOptions;
    private readonly ILogger<ConnectionScheduleProvisioner> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IChannelJobTypeRegistry _jobTypes;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionScheduleProvisioner"/> class.
    /// </summary>
    /// <param name="connectionRepository">Connection repository.</param>
    /// <param name="scheduleRepository">Job schedule repository.</param>
    /// <param name="tenantContext">Ambient tenant context, used for diagnostics.</param>
    /// <param name="backgroundJobsOptions">Background jobs options.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="timeProvider">Time provider.</param>
    /// <param name="jobTypes">Registry of the job types each channel serves.</param>
    public ConnectionScheduleProvisioner(
        IConnectionRepository connectionRepository,
        IJobScheduleRepository scheduleRepository,
        ITenantContext tenantContext,
        IOptions<BackgroundJobsOptions> backgroundJobsOptions,
        ILogger<ConnectionScheduleProvisioner> logger,
        TimeProvider timeProvider,
        IChannelJobTypeRegistry jobTypes)
    {
        _connectionRepository = connectionRepository;
        _scheduleRepository = scheduleRepository;
        _tenantContext = tenantContext;
        _backgroundJobsOptions = backgroundJobsOptions.Value;
        _logger = logger;
        _timeProvider = timeProvider;
        _jobTypes = jobTypes;
    }

    /// <summary>
    /// Provisions the orders schedules of the connection announced by the event. Persistence
    /// failures are not swallowed here: a duplicate (ConnectionId, JobType) row is an
    /// infrastructure concern that the calling adapter recognises and absorbs.
    /// </summary>
    /// <param name="message">Connection-created event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ProvisionAsync(ConnectionCreatedEvent message, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ConnectionScheduleProvisioner: ConnectionId={ConnectionId}, ChannelId={ChannelId}, TenantId={TenantId}",
            message.ConnectionId,
            message.ChannelId,
            _tenantContext.TenantId);

        Connection? connection = await _connectionRepository
            .FindByIdWithTemplateAsync(message.ConnectionId, cancellationToken);

        if (connection == null || !connection.IsActive)
        {
            _logger.LogWarning(
                "ConnectionScheduleProvisioner: connection {ConnectionId} not found in tenant {TenantId} or it is inactive, " +
                "no schedules created.",
                message.ConnectionId,
                _tenantContext.TenantId);
            return;
        }

        string? channelCode = connection.SystemChannelTemplate?.Code;
        IReadOnlyList<JobType> jobTypes = _jobTypes.GetJobTypes(channelCode, ChannelJobKind.OrdersImport);

        if (jobTypes.Count == 0)
        {
            _logger.LogInformation(
                "ConnectionScheduleProvisioner: channel '{ChannelCode}' has no orders import job types, " +
                "no schedules created for connection {ConnectionId}.",
                channelCode,
                message.ConnectionId);
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();

        // NextRunAt = now rather than the next cron occurrence: the scheduler fires the schedule on
        // its next tick, so the first import starts within a minute of the connection appearing
        // instead of at the top of the next hour.
        List<JobSchedule> schedules = jobTypes
            .Select(jobType => new JobSchedule
            {
                Id = Guid.NewGuid(),
                ConnectionId = connection.Id,
                JobType = jobType,
                CronExpression = _backgroundJobsOptions.DefaultOrdersImportCronExpression,
                IsEnabled = true,
                LastRunAt = null,
                NextRunAt = now,
            })
            .ToList();

        int createdCount = await _scheduleRepository
            .AddMissingAsync(connection.Id, schedules, cancellationToken);

        if (createdCount == 0)
        {
            _logger.LogInformation(
                "ConnectionScheduleProvisioner: connection {ConnectionId} already has every orders schedule, " +
                "nothing created.",
                message.ConnectionId);
            return;
        }

        _logger.LogInformation(
            "ConnectionScheduleProvisioner: created {Count} JobSchedule(s) for connection {ConnectionId} " +
            "(tenant {TenantId}, cron '{CronExpression}'), first run due immediately.",
            createdCount,
            message.ConnectionId,
            _tenantContext.TenantId,
            _backgroundJobsOptions.DefaultOrdersImportCronExpression);
    }
}
