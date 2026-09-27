using Delobytes.App.Backend.Integrations.Application.DTOs.SyncJobs;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Integrations.Application.Queries.GetSyncJobs;

public class GetSyncJobsQueryHandler : IRequestHandler<GetSyncJobsQuery, GetSyncJobsResponse>
{
    private readonly ISyncJobRepository _syncJobRepository;

    public GetSyncJobsQueryHandler(ISyncJobRepository syncJobRepository)
    {
        _syncJobRepository = syncJobRepository;
    }

    public async Task<GetSyncJobsResponse> Handle(
        GetSyncJobsQuery request,
        CancellationToken cancellationToken)
    {
        List<SyncJob> jobs = await _syncJobRepository
            .GetProductsImportJobsAsync(cancellationToken);

        List<SyncJobDto> items = jobs
            .Select(j => MapToDto(j))
            .ToList();

        return new GetSyncJobsResponse { Items = items };
    }

    private static SyncJobDto MapToDto(SyncJob job)
    {
        return new SyncJobDto
        {
            Id = job.Id,
            ConnectionId = job.ConnectionId,
            JobType = job.JobType.ToString(),
            Status = job.Status.ToString(),
            CreatedAt = job.CreatedAt,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt,
            ErrorMessage = job.ErrorMessage,
            RecordsProcessed = job.RecordsProcessed,
            RecordsImported = job.RecordsImported,
            RecordsCreated = job.RecordsCreated,
            RecordsUpdated = job.RecordsUpdated,
            RecordsSkipped = job.RecordsSkipped,
            RecordsFailed = job.RecordsFailed,
            RequestedByUserId = job.RequestedByUserId,
        };
    }
}
