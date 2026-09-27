using Delobytes.App.Backend.Integrations.Application.DTOs.SyncJobs;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Integrations.Application.Queries.GetSyncJob;

public class GetSyncJobQueryHandler : IRequestHandler<GetSyncJobQuery, GetSyncJobResponse>
{
    private readonly ISyncJobRepository _syncJobRepository;

    public GetSyncJobQueryHandler(ISyncJobRepository syncJobRepository)
    {
        _syncJobRepository = syncJobRepository;
    }

    public async Task<GetSyncJobResponse> Handle(
        GetSyncJobQuery request,
        CancellationToken cancellationToken)
    {
        SyncJob? syncJob = await _syncJobRepository.FindByIdAsync(request.SyncJobId, cancellationToken);

        if (syncJob == null)
        {
            throw new KeyNotFoundException($"Задача импорта с ID '{request.SyncJobId}' не найдена.");
        }

        return new GetSyncJobResponse
        {
            Job = new SyncJobDto
            {
                Id = syncJob.Id,
                ConnectionId = syncJob.ConnectionId,
                JobType = syncJob.JobType.ToString(),
                Status = syncJob.Status.ToString(),
                CreatedAt = syncJob.CreatedAt,
                StartedAt = syncJob.StartedAt,
                CompletedAt = syncJob.CompletedAt,
                ErrorMessage = syncJob.ErrorMessage,
                RecordsProcessed = syncJob.RecordsProcessed,
                RecordsImported = syncJob.RecordsImported,
                RecordsCreated = syncJob.RecordsCreated,
                RecordsUpdated = syncJob.RecordsUpdated,
                RecordsSkipped = syncJob.RecordsSkipped,
                RecordsFailed = syncJob.RecordsFailed,
                RequestedByUserId = syncJob.RequestedByUserId,
            },
        };
    }
}
