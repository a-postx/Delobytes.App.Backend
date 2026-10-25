using MediatR;
using Delobytes.App.Backend.Integrations.Application.Events;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Application.Commands;

public class StartOrdersImportCommand : IRequest<StartOrdersImportResponse>
{
    public Guid ConnectionId { get; set; }
}

public class StartOrdersImportResponse
{
    public Guid JobRunId { get; set; }
}

/// <summary>
/// Creates a JobRun for an orders import cycle and publishes the internal event that triggers
/// ProcessOrdersImportConsumer (Stage 9). Orders import has no manual HTTP trigger (decision 14):
/// the scheduler (Stage 11) is the sole caller, so RequestedByUserId is always null here.
/// </summary>
public class StartOrdersImportCommandHandler : IRequestHandler<StartOrdersImportCommand, StartOrdersImportResponse>
{
    private readonly IConnectionRepository _connectionRepository;
    private readonly IJobRunRepository _jobRunRepository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<StartOrdersImportCommandHandler> _logger;

    public StartOrdersImportCommandHandler(
        IConnectionRepository connectionRepository,
        IJobRunRepository jobRunRepository,
        IEventPublisher eventPublisher,
        ILogger<StartOrdersImportCommandHandler> logger)
    {
        _connectionRepository = connectionRepository;
        _jobRunRepository = jobRunRepository;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<StartOrdersImportResponse> Handle(
        StartOrdersImportCommand request,
        CancellationToken cancellationToken)
    {
        Connection? connection = await _connectionRepository
            .FindByIdAsync(request.ConnectionId, cancellationToken);

        if (connection == null)
        {
            throw new KeyNotFoundException($"Подключение с ID '{request.ConnectionId}' не найдено.");
        }

        if (!connection.IsActive)
        {
            throw new InvalidOperationException("Подключение неактивно. Импорт заказов невозможен.");
        }

        bool hasActiveJob = await _jobRunRepository
            .HasActiveJobAsync(request.ConnectionId, JobType.OrdersImport, cancellationToken);

        if (hasActiveJob)
        {
            throw new ConflictException(
                "Для этого подключения уже есть активная задача импорта заказов (Pending или Running).");
        }

        JobRun jobRun = new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = request.ConnectionId,
            JobType = JobType.OrdersImport,
            Status = JobStatus.Pending,
            RequestedByUserId = null,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await _jobRunRepository.CreateAsync(jobRun, cancellationToken);

        await _jobRunRepository.CreateOrdersImportJobAsync(
            new OrdersImportJob { JobRunId = jobRun.Id },
            cancellationToken);

        // SyncCheckpoint is intentionally not bootstrapped here: ProcessOrdersImportConsumer
        // (Stage 9) treats a missing checkpoint as "first run" and derives the 30-day backfill
        // window itself, so there is nothing for this handler to pre-create.
        _logger.LogInformation(
            "Создана задача импорта заказов {JobRunId} для подключения {ConnectionId}",
            jobRun.Id,
            request.ConnectionId);

        OrdersImportRequestedEvent importEvent = new OrdersImportRequestedEvent
        {
            JobRunId = jobRun.Id,
            ConnectionId = request.ConnectionId,
        };

        try
        {
            await _eventPublisher.PublishAsync(importEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            // JobRun уже сохранён, но событие не опубликовано — задача зависнет в Pending.
            // Это известный риск (outbox не реализован). Логируем предупреждение.
            _logger.LogWarning(
                ex,
                "Не удалось опубликовать OrdersImportRequestedEvent для JobRun {JobRunId}. " +
                "Задача останется в статусе Pending до ручного вмешательства.",
                jobRun.Id);
        }

        return new StartOrdersImportResponse
        {
            JobRunId = jobRun.Id,
        };
    }
}
