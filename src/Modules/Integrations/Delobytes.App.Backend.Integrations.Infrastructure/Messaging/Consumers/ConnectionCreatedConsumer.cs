using Delobytes.App.Backend.Integrations.Application.Jobs;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using static MassTransit.Monitoring.Performance.BuiltInCounters;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// MassTransit consumer for <see cref="ConnectionCreatedEvent"/>: hands the work to
/// <see cref="ConnectionScheduleProvisioner"/> and absorbs the one persistence failure the
/// provisioner must not know about — a duplicate (ConnectionId, JobType) row, which Postgres
/// reports as SQLSTATE 23505.
/// </summary>
public class ConnectionCreatedConsumer : IConsumer<ConnectionCreatedEvent>
{
    private const string UniqueViolationSqlState = "23505";

    private readonly ConnectionScheduleProvisioner _scheduleProvisioner;
    private readonly ILogger<ConnectionCreatedConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionCreatedConsumer"/> class.
    /// </summary>
    /// <param name="provisioner">Application-service that builds and stores the schedules.</param>
    /// <param name="logger">Logger instance.</param>
    public ConnectionCreatedConsumer(
        ConnectionScheduleProvisioner provisioner,
        ILogger<ConnectionCreatedConsumer> logger)
    {
        _scheduleProvisioner = provisioner;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<ConnectionCreatedEvent> context)
    {
        try
        {
            await _scheduleProvisioner.ProvisionAsync(context.Message, context.CancellationToken);
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
                context.Message.ConnectionId);
        }
    }

    private static bool IsUniqueViolation(Exception exception)
    {
        return exception is DbUpdateException { InnerException: PostgresException { SqlState: UniqueViolationSqlState } }
            || exception is PostgresException { SqlState: UniqueViolationSqlState };
    }
}
