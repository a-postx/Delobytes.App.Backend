using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Events;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Jobs;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Application.Commands;

public class StartProductsImportCommand : IRequest<StartProductsImportResponse>
{
    public Guid ConnectionId { get; set; }
}

public class StartProductsImportResponse
{
    public Guid JobRunId { get; set; }
}

public class StartProductsImportCommandHandler : IRequestHandler<StartProductsImportCommand, StartProductsImportResponse>
{
    private readonly IConnectionRepository _connectionRepository;
    private readonly IJobRunRepository _jobRunRepository;
    private readonly ISyncCheckpointRepository _syncCheckpointRepository;
    private readonly IEventPublisher _eventPublisher;
    private readonly JobRunStarter _jobRunStarter;
    private readonly IChannelJobTypeRegistry _jobTypeRegistry;
    private readonly IUserContext _userContext;
    private readonly ILogger<StartProductsImportCommandHandler> _logger;

    public StartProductsImportCommandHandler(
        IConnectionRepository connectionRepository,
        IJobRunRepository jobRunRepository,
        ISyncCheckpointRepository syncCheckpointRepository,
        IEventPublisher eventPublisher,
        JobRunStarter jobRunStarter,
        IChannelJobTypeRegistry jobTypeRegistry,
        IUserContext userContext,
        ILogger<StartProductsImportCommandHandler> logger)
    {
        _connectionRepository = connectionRepository;
        _jobRunRepository = jobRunRepository;
        _syncCheckpointRepository = syncCheckpointRepository;
        _eventPublisher = eventPublisher;
        _jobRunStarter = jobRunStarter;
        _jobTypeRegistry = jobTypeRegistry;
        _userContext = userContext;
        _logger = logger;
    }

    public async Task<StartProductsImportResponse> Handle(
        StartProductsImportCommand request,
        CancellationToken cancellationToken)
    {
        Connection? connection = await _connectionRepository
            .FindByIdWithTemplateAsync(request.ConnectionId, cancellationToken) ?? throw new KeyNotFoundException($"Подключение с ID '{request.ConnectionId}' не найдено.");

        if (!connection.IsActive)
        {
            throw new InvalidOperationException("Подключение неактивно. Импорт невозможен.");
        }

        // The channel module, not this handler, decides which job type its products import runs as:
        // the same orchestration is served under a different type per channel.
        string? channelCode = connection.SystemChannelTemplate?.Code;

        IReadOnlyList<JobType> productsJobTypes = _jobTypeRegistry.GetJobTypes(
            channelCode,
            ChannelJobKind.ProductsImport);

        if (productsJobTypes.Count == 0)
        {
            throw new InvalidOperationException(
                $"Импорт карточек товаров не поддерживается для канала '{channelCode}'.");
        }

        if (productsJobTypes.Count > 1)
        {
            // Two products types for one channel is a registry design error: picking one would
            // silently create the wrong run type and break cancellation and dispatch for it.
            throw new InvalidOperationException(
                $"Канал '{channelCode}' объявляет несколько типов задач импорта товаров: " +
                $"{string.Join(", ", productsJobTypes)}.");
        }

        JobType productsJobType = productsJobTypes[0];

        bool hasActiveJob = await _jobRunRepository
            .HasActiveJobAsync(request.ConnectionId, productsJobType, cancellationToken);

        if (hasActiveJob)
        {
            throw new ConflictException(
                "Для этого подключения уже есть активная задача импорта (Pending или Running).");
        }

        JobRun jobRun = await _jobRunStarter.CreatePendingAsync(
            request.ConnectionId,
            productsJobType,
            _userContext.UserId,
            cancellationToken);

        // Bootstraps the watermark only on the very first run for this (ConnectionId, JobType):
        // an existing checkpoint already carries the real watermark from a previous run and
        // must not be reset back to the 30-day backfill window.
        SyncCheckpoint? checkpoint = await _syncCheckpointRepository
            .GetAsync(request.ConnectionId, productsJobType, cancellationToken);

        if (checkpoint == null)
        {
            await _syncCheckpointRepository.UpsertAsync(
                new SyncCheckpoint
                {
                    Id = Guid.NewGuid(),
                    ConnectionId = request.ConnectionId,
                    JobType = productsJobType,
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
