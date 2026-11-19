using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Application.Commands;

public class DeleteConnectionCommand : IRequest
{
    public Guid Id { get; set; }
}

/// <summary>
/// Deactivates a connection (soft delete) together with its job schedules. The associated
/// Catalog.Channel is intentionally NOT touched — Channel is an independent business entity owned by
/// Catalog and its lifecycle no longer depends on connection status. Historical data collected while
/// the connection was active remains fully accessible for the channel.
/// </summary>
public class DeleteConnectionCommandHandler : IRequestHandler<DeleteConnectionCommand>
{
    private readonly IConnectionRepository _connectionRepository;
    private readonly IJobScheduleRepository _scheduleRepository;
    private readonly ILogger<DeleteConnectionCommandHandler> _logger;

    public DeleteConnectionCommandHandler(
        IConnectionRepository connectionRepository,
        IJobScheduleRepository scheduleRepository,
        ILogger<DeleteConnectionCommandHandler> logger)
    {
        _connectionRepository = connectionRepository;
        _scheduleRepository = scheduleRepository;
        _logger = logger;
    }

    public async Task Handle(DeleteConnectionCommand request, CancellationToken cancellationToken)
    {
        Connection? connection = await _connectionRepository
            .FindByIdWithTemplateAsync(request.Id, cancellationToken) ?? throw new KeyNotFoundException($"Подключение с ID '{request.Id}' не найдено.");

        connection.IsActive = false;
        connection.UpdatedAt = DateTimeOffset.UtcNow;

        await _connectionRepository.SaveChangesAsync(cancellationToken);

        // Without this the scheduler keeps firing the connection's schedules every cron occurrence
        // and only skips them, because nothing else ever disables a schedule.
        int disabledSchedules = await _scheduleRepository.DisableForConnectionAsync(connection.Id, cancellationToken);

        _logger.LogInformation(
            "Deleted connection {ConnectionId}: deactivated and disabled {ScheduleCount} schedule(s).",
            connection.Id,
            disabledSchedules);
    }
}
