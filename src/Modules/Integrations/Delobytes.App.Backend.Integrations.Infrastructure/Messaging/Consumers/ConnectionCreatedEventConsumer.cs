using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Extensions;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// Creates the orders <see cref="JobSchedule"/> rows for a connection created while the application
/// is running, so adding a connection schedules its import immediately instead of at the next
/// restart.
public class ConnectionCreatedEventConsumer
{
    private const string UniqueViolationSqlState = "23505";

    private readonly IConnectionRepository _connectionRepository;
    private readonly IJobScheduleRepository _scheduleRepository;
    private readonly ITenantContext _tenantContext;
    private readonly BackgroundJobsOptions _backgroundJobsOptions;
    private readonly ILogger<ConnectionCreatedEventConsumer> _logger;
    private readonly TimeProvider _timeProvider;

    public ConnectionCreatedEventConsumer(
        IConnectionRepository connectionRepository,
        IJobScheduleRepository scheduleRepository,
        ITenantContext tenantContext,
        IOptions<BackgroundJobsOptions> backgroundJobsOptions,
        ILogger<ConnectionCreatedEventConsumer> logger,
        TimeProvider timeProvider)
    {
        _connectionRepository = connectionRepository;
        _scheduleRepository = scheduleRepository;
        _tenantContext = tenantContext;
        _backgroundJobsOptions = backgroundJobsOptions.Value;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task ProcessAsync(ConnectionCreatedEvent message, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ConnectionCreatedEventConsumer: ConnectionId={ConnectionId}, ChannelId={ChannelId}, TenantId={TenantId}",
            message.ConnectionId,
            message.ChannelId,
            _tenantContext.TenantId);

        Connection? connection = await _connectionRepository
            .FindByIdWithTemplateAsync(message.ConnectionId, cancellationToken);

        if (connection == null || !connection.IsActive)
        {
            _logger.LogWarning(
                "ConnectionCreatedEventConsumer: connection {ConnectionId} not found in tenant {TenantId} or it is inactive, " +
                "no schedules created.",
                message.ConnectionId,
                _tenantContext.TenantId);
            return;
        }

        string? channelCode = connection.SystemChannelTemplate?.Code;
        IReadOnlyList<JobType> jobTypes = OrdersJobTypeMap.GetOrdersJobTypes(channelCode);

        if (jobTypes.Count == 0)
        {
            _logger.LogInformation(
                "ConnectionCreatedEventConsumer: channel '{ChannelCode}' has no orders import job types, " +
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

        int createdCount;

        try
        {
            createdCount = await _scheduleRepository
                .AddMissingAsync(connection.Id, schedules, cancellationToken);
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            // A redelivery racing the original delivery, or the startup seeder inserting between the
            // existence check and the save: the unique (ConnectionId, JobType) index rejects the
            // duplicate, and the rows that already exist are exactly what was wanted here.
            _logger.LogWarning(
                ex,
                "ConnectionCreatedEventConsumer: schedules for connection {ConnectionId} already exist, " +
                "keeping the existing rows.",
                message.ConnectionId);
            return;
        }

        if (createdCount == 0)
        {
            _logger.LogInformation(
                "ConnectionCreatedEventConsumer: connection {ConnectionId} already has every orders schedule, " +
                "nothing created.",
                message.ConnectionId);
            return;
        }

        _logger.LogInformation(
            "ConnectionCreatedEventConsumer: created {Count} JobSchedule(s) for connection {ConnectionId} " +
            "(tenant {TenantId}, cron '{CronExpression}'), first run due immediately.",
            createdCount,
            message.ConnectionId,
            _tenantContext.TenantId,
            _backgroundJobsOptions.DefaultOrdersImportCronExpression);
    }

    private static bool IsUniqueViolation(Exception exception)
    {
        return exception is DbUpdateException { InnerException: PostgresException { SqlState: UniqueViolationSqlState } }
            || exception is PostgresException { SqlState: UniqueViolationSqlState };
    }
}
