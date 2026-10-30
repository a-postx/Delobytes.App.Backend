using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Creates the Pending follow-up job run that resumes an orders import stopped by throttle or
/// budget, guarding against a duplicate when a pending continuation already exists.
/// </summary>
public sealed class OrdersContinuationScheduler
{
    private readonly IJobRunRepository _jobRunRepository;
    private readonly TimeProvider _timeProvider;

    public OrdersContinuationScheduler(IJobRunRepository jobRunRepository, TimeProvider timeProvider)
    {
        _jobRunRepository = jobRunRepository;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Returns the created continuation, or null when a Pending job run for this connection
    /// already exists and a redelivery must not create a duplicate.
    /// </summary>
    public async Task<JobRun?> ScheduleAsync(JobRun current, DateTimeOffset resumeAt, CancellationToken cancellationToken)
    {
        bool hasPending = await _jobRunRepository
            .HasPendingJobAsync(current.ConnectionId, JobType.OrdersImport, current.Id, cancellationToken);

        if (hasPending)
        {
            return null;
        }

        JobRun continuation = new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = current.ConnectionId,
            JobType = JobType.OrdersImport,
            Status = JobStatus.Pending,
            CreatedAt = _timeProvider.GetUtcNow(),
            NotBefore = resumeAt,
        };

        await _jobRunRepository.CreateAsync(continuation, cancellationToken);

        return continuation;
    }
}
