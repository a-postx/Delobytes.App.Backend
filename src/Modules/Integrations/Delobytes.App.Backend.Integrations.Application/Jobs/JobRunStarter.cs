using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Application.Jobs;

/// <summary>
/// Creates a Pending <see cref="JobRun"/> together with the type-specific row its job type needs.
/// Both entry points that start a run — the manual start commands and the scheduler — go through
/// here, so the "which rows does this job type require" rule lives in one place.
/// </summary>
public sealed class JobRunStarter
{
    private readonly IJobRunRepository _jobRunRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<JobRunStarter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobRunStarter"/> class.
    /// </summary>
    /// <param name="jobRunRepository">Job run repository.</param>
    /// <param name="timeProvider">Time provider.</param>
    /// <param name="logger">Logger instance.</param>
    public JobRunStarter(
        IJobRunRepository jobRunRepository,
        TimeProvider timeProvider,
        ILogger<JobRunStarter> logger)
    {
        _jobRunRepository = jobRunRepository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Creates and persists a Pending run of the given type, plus any type-specific row it needs.
    /// No dispatch happens here: the caller publishes the event or leaves the run for the worker.
    /// </summary>
    /// <param name="connectionId">Connection the run imports through.</param>
    /// <param name="jobType">Type of the run.</param>
    /// <param name="requestedByUserId">User who requested the run, or null for scheduler-driven runs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The persisted run.</returns>
    public async Task<JobRun> CreatePendingAsync(
        Guid connectionId,
        JobType jobType,
        Guid? requestedByUserId,
        CancellationToken cancellationToken)
    {
        JobRun jobRun = new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            JobType = jobType,
            Status = JobStatus.Pending,
            RequestedByUserId = requestedByUserId,
            CreatedAt = _timeProvider.GetUtcNow(),
        };

        await _jobRunRepository.CreateAsync(jobRun, cancellationToken);

        switch (jobType)
        {
            case JobType.ProductsImport:
                await _jobRunRepository.CreateProductsImportJobAsync(
                    new ProductsImportJob { JobRunId = jobRun.Id },
                    cancellationToken);
                break;

            // Orders import keeps its cursor in OrdersBackfillState/SyncCheckpoint rather than
            // in a type-specific JobRun row, so nothing is created here on purpose.
            case JobType.WbOrdersImport:
            case JobType.OzonOrdersImportFbo:
            case JobType.OzonOrdersImportFbs:
                break;

            default:
                _logger.LogWarning(
                    "JobRun {JobRunId} was created for JobType {JobType} without type-specific data: " +
                    "no such row is provisioned for this type.",
                    jobRun.Id,
                    jobType);
                break;
        }

        return jobRun;
    }
}
