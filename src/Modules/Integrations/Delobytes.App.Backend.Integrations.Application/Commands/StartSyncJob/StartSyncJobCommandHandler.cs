using Delobytes.App.Backend.Integrations.Application.Events;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Application.Commands.StartSyncJob;

/// <summary>
/// Handler for StartSyncJobCommand.
/// </summary>
public class StartSyncJobCommandHandler : IRequestHandler<StartSyncJobCommand, StartSyncJobResponse>
{
    private readonly ISyncJobRepository _syncJobRepository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<StartSyncJobCommandHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StartSyncJobCommandHandler"/> class.
    /// </summary>
    /// <param name="syncJobRepository">Sync job repository.</param>
    /// <param name="eventPublisher">Event publisher.</param>
    /// <param name="logger">Logger instance.</param>
    public StartSyncJobCommandHandler(
        ISyncJobRepository syncJobRepository,
        IEventPublisher eventPublisher,
        ILogger<StartSyncJobCommandHandler> logger)
    {
        _syncJobRepository = syncJobRepository;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<StartSyncJobResponse> Handle(StartSyncJobCommand request, CancellationToken cancellationToken)
    {
        SyncJob syncJob = new SyncJob
        {
            Id = Guid.NewGuid(),
            ConnectionId = request.ConnectionId,
            JobType = JobType.OrdersSync,
            Status = SyncJobStatus.Pending,
            DateRangeFrom = request.DateRangeFrom,
            DateRangeTo = request.DateRangeTo,
            RecordsProcessed = 0,
            RecordsImported = 0
        };

        _syncJobRepository.Add(syncJob);
        await _syncJobRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created SyncJob {SyncJobId} for Connection {ConnectionId}", syncJob.Id, request.ConnectionId);

        StartSyncJobEvent syncEvent = new StartSyncJobEvent
        {
            SyncJobId = syncJob.Id,
            ConnectionId = request.ConnectionId
        };

        await _eventPublisher.PublishAsync(syncEvent, cancellationToken);

        _logger.LogInformation("Published StartSyncJobEvent for SyncJob {SyncJobId}", syncJob.Id);

        return new StartSyncJobResponse
        {
            SyncJobId = syncJob.Id
        };
    }
}
