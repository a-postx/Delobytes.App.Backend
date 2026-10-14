using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Events;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Application.Commands.StartProductsImport;

public class StartProductsImportCommandHandler : IRequestHandler<StartProductsImportCommand, StartProductsImportResponse>
{
    private readonly IConnectionRepository _connectionRepository;
    private readonly IJobRunRepository _jobRunRepository;
    private readonly ISyncCheckpointRepository _syncCheckpointRepository;
    private readonly IEventPublisher _eventPublisher;
    private readonly IUserContext _userContext;
    private readonly ILogger<StartProductsImportCommandHandler> _logger;

    public StartProductsImportCommandHandler(
        IConnectionRepository connectionRepository,
        IJobRunRepository jobRunRepository,
        ISyncCheckpointRepository syncCheckpointRepository,
        IEventPublisher eventPublisher,
        IUserContext userContext,
        ILogger<StartProductsImportCommandHandler> logger)
    {
        _connectionRepository = connectionRepository;
        _jobRunRepository = jobRunRepository;
        _syncCheckpointRepository = syncCheckpointRepository;
        _eventPublisher = eventPublisher;
        _userContext = userContext;
        _logger = logger;
    }

    public async Task<StartProductsImportResponse> Handle(
        StartProductsImportCommand request,
        CancellationToken cancellationToken)
    {
        Connection? connection = await _connectionRepository
            .FindByIdWithTemplateAsync(request.ConnectionId, cancellationToken);

        if (connection == null)
        {
            throw new KeyNotFoundException($"Подключение с ID '{request.ConnectionId}' не найдено.");
        }

        if (!connection.IsActive)
        {
            throw new InvalidOperationException("Подключение неактивно. Импорт невозможен.");
        }

        // Wildberries — единственный поддерживаемый источник в первой версии.
        if (!string.Equals(
                connection.SystemChannelTemplate?.Code,
                "wildberries",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Импорт карточек товаров не поддерживается для канала '{connection.SystemChannelTemplate?.Code}'.");
        }

        bool hasActiveJob = await _jobRunRepository
            .HasActiveJobAsync(request.ConnectionId, JobType.ProductsImport, cancellationToken);

        if (hasActiveJob)
        {
            throw new ConflictException(
                "Для этого подключения уже есть активная задача импорта (Pending или Running).");
        }

        JobRun jobRun = new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = request.ConnectionId,
            JobType = JobType.ProductsImport,
            Status = JobStatus.Pending,
            RequestedByUserId = _userContext.UserId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await _jobRunRepository.CreateAsync(jobRun, cancellationToken);

        await _jobRunRepository.CreateProductsImportJobAsync(
            new ProductsImportJob { JobRunId = jobRun.Id },
            cancellationToken);

        // Bootstraps the watermark only on the very first run for this (ConnectionId, JobType):
        // an existing checkpoint already carries the real watermark from a previous run and
        // must not be reset back to the 30-day backfill window.
        SyncCheckpoint? checkpoint = await _syncCheckpointRepository
            .GetAsync(request.ConnectionId, JobType.ProductsImport, cancellationToken);

        if (checkpoint == null)
        {
            await _syncCheckpointRepository.UpsertAsync(
                new SyncCheckpoint
                {
                    Id = Guid.NewGuid(),
                    ConnectionId = request.ConnectionId,
                    JobType = JobType.ProductsImport,
                    LastChangedAt = null,
                    LastSuccessAt = DateTimeOffset.UtcNow.AddDays(-30),
                    LastJobRunId = null,
                },
                cancellationToken);
        }

        _logger.LogInformation(
            "Создана задача импорта {JobRunId} для подключения {ConnectionId}",
            jobRun.Id,
            request.ConnectionId);

        ProductsImportRequestedEvent importEvent = new ProductsImportRequestedEvent
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
                "Не удалось опубликовать ProductsImportRequestedEvent для JobRun {JobRunId}. " +
                "Задача останется в статусе Pending до ручного вмешательства.",
                jobRun.Id);
        }

        return new StartProductsImportResponse
        {
            JobRunId = jobRun.Id,
        };
    }
}
