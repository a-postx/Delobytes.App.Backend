using Delobytes.App.Backend.Integrations.Application.DTOs.Jobs;
using Delobytes.App.Backend.Integrations.Application.Extensions;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Integrations.Application.Queries.GetJob;

public class GetJobQueryHandler : IRequestHandler<GetJobQuery, JobDto>
{
    private readonly IJobRunRepository _jobRunRepository;

    public GetJobQueryHandler(IJobRunRepository jobRunRepository)
    {
        _jobRunRepository = jobRunRepository;
    }

    public async Task<JobDto> Handle(GetJobQuery request, CancellationToken cancellationToken)
    {
        JobRun? jobRun = await _jobRunRepository.GetByIdAsync(request.JobRunId, cancellationToken);

        if (jobRun == null)
        {
            throw new KeyNotFoundException($"Задача с ID '{request.JobRunId}' не найдена.");
        }

        return new JobDto
        {
            Id = jobRun.Id,
            Type = jobRun.JobType.ToStringCamelCase(),
            ConnectionId = jobRun.ConnectionId,
            Status = jobRun.Status,
            RecordsCreated = jobRun.RecordsCreated,
            RecordsUpdated = jobRun.RecordsUpdated,
            RecordsSkipped = jobRun.RecordsSkipped,
            RecordsFailed = jobRun.RecordsFailed,
            CreatedAt = jobRun.CreatedAt,
            StartedAt = jobRun.StartedAt,
            CompletedAt = jobRun.CompletedAt,
            Error = jobRun.Error,
        };
    }
}
