using Delobytes.App.Backend.Integrations.Application.DTOs.Jobs;
using Delobytes.App.Backend.Integrations.Application.Extensions;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Integrations.Application.Queries.GetJobs;

public class GetJobsQueryHandler : IRequestHandler<GetJobsQuery, GetJobsResponse>
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;
    private const int MinPageSize = 1;

    private readonly IJobRunRepository _jobRunRepository;

    public GetJobsQueryHandler(IJobRunRepository jobRunRepository)
    {
        _jobRunRepository = jobRunRepository;
    }

    public async Task<GetJobsResponse> Handle(GetJobsQuery request, CancellationToken cancellationToken)
    {
        JobType? jobType = null;

        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            if (!JobTypeExtensions.TryFromString(request.Type, out JobType parsed))
            {
                throw new InvalidOperationException(
                    $"Неизвестный тип задачи '{request.Type}'. Допустимые значения: " +
                    $"'{JobTypeExtensions.ProductsImport}', '{JobTypeExtensions.OrdersImport}', '{JobTypeExtensions.OrdersSync}'.");
            }

            jobType = parsed;
        }

        int page = Math.Max(request.Page ?? 1, 1);
        int pageSize = Math.Clamp(request.PageSize ?? DefaultPageSize, MinPageSize, MaxPageSize);
        int? skip = request.Page.HasValue ? (page - 1) * pageSize : null;
        int? take = request.Page.HasValue ? pageSize : null;

        JobStatus[]? statuses = request.Status is { Length: > 0 } ? request.Status : null;

        (int totalCount, List<JobRun> jobRuns) = await _jobRunRepository.GetPagedAsync(
            jobType,
            request.ConnectionId,
            statuses,
            skip,
            take,
            cancellationToken);

        List<JobDto> jobs = jobRuns.Select(MapToDto).ToList();

        return new GetJobsResponse
        {
            Jobs = jobs,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    private static JobDto MapToDto(JobRun jobRun)
    {
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
