using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Application.Commands.StartProductsImport;

public class StartProductsImportCommandHandler : IRequestHandler<StartProductsImportCommand, StartProductsImportResponse>
{
    private readonly IConnectionRepository _connectionRepository;
    private readonly ISyncJobRepository _syncJobRepository;
    private readonly IEventPublisher _eventPublisher;
    private readonly IUserContext _userContext;
    private readonly ILogger<StartProductsImportCommandHandler> _logger;

    public StartProductsImportCommandHandler(
        IConnectionRepository connectionRepository,
        ISyncJobRepository syncJobRepository,
        IEventPublisher eventPublisher,
        IUserContext userContext,
        ILogger<StartProductsImportCommandHandler> logger)
    {
        _connectionRepository = connectionRepository;
        _syncJobRepository = syncJobRepository;
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

        bool hasDuplicate = await _syncJobRepository
            .HasActivePendingOrRunningJobForConnectionAsync(request.ConnectionId, cancellationToken);

        if (hasDuplicate)
        {
            throw new ConflictException(
                "Для этого подключения уже есть активная задача импорта (Pending или Running).");
        }

        SyncJob syncJob = new SyncJob
        {
            Id = Guid.NewGuid(),
            ConnectionId = request.ConnectionId,
            JobType = JobType.ProductsImport,
            Status = SyncJobStatus.Pending,
            DateRangeFrom = DateTimeOffset.MinValue,
            DateRangeTo = DateTimeOffset.MinValue,
            RecordsProcessed = 0,
            RecordsImported = 0,
            RecordsCreated = 0,
            RecordsUpdated = 0,
            RecordsSkipped = 0,
            RecordsFailed = 0,
            RequestedByUserId = _userContext.UserId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _syncJobRepository.Add(syncJob);
        await _syncJobRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Создана задача импорта {SyncJobId} для подключения {ConnectionId}",
            syncJob.Id,
            request.ConnectionId);

        ProductsImportRequestedEvent importEvent = new ProductsImportRequestedEvent
        {
            SyncJobId = syncJob.Id,
            ConnectionId = request.ConnectionId,
        };

        try
        {
            await _eventPublisher.PublishAsync(importEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            // SyncJob уже сохранён, но событие не опубликовано — задача зависнет в Pending.
            // Это известный риск (outbox не реализован). Логируем предупреждение.
            _logger.LogWarning(
                ex,
                "Не удалось опубликовать ProductsImportRequestedEvent для SyncJob {SyncJobId}. " +
                "Задача останется в статусе Pending до ручного вмешательства.",
                syncJob.Id);
        }

        return new StartProductsImportResponse
        {
            SyncJobId = syncJob.Id,
        };
    }
}
