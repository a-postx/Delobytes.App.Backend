using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Application.Jobs;

/// <summary>
/// Shared JobRun status transitions: the Pending to Running start and the terminal Failed write.
/// </summary>
public sealed class JobRunLifecycle
{
    private readonly IJobRunRepository _jobRunRepository;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobRunLifecycle"/> class.
    /// </summary>
    /// <param name="jobRunRepository">Job run repository.</param>
    /// <param name="timeProvider">Time provider.</param>
    public JobRunLifecycle(IJobRunRepository jobRunRepository, TimeProvider timeProvider)
    {
        _jobRunRepository = jobRunRepository;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Marks the job run Failed and persists it. Always writes with <see cref="CancellationToken.None"/>:
    /// the consumer's own token may already be cancelled, but the failure must still be recorded.
    /// </summary>
    /// <param name="jobRun">Run to fail.</param>
    /// <param name="error">Failure reason.</param>
    /// <param name="cancellationToken">Unused; the write deliberately bypasses the caller's token.</param>
    public async Task FailAsync(JobRun jobRun, string error, CancellationToken cancellationToken)
    {
        jobRun.Status = JobStatus.Failed;
        jobRun.Error = error;
        jobRun.CompletedAt = _timeProvider.GetUtcNow();

        await _jobRunRepository.UpdateAsync(jobRun, CancellationToken.None);
    }

    /// <summary>
    /// Transitions a Pending job run to Running and persists it. A job run that is already
    /// Running is left untouched, so a redelivery does not reset its start instant.
    /// </summary>
    /// <param name="jobRun">Run to start.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StartAsync(JobRun jobRun, CancellationToken cancellationToken)
    {
        if (jobRun.Status != JobStatus.Pending)
        {
            return;
        }

        jobRun.Status = JobStatus.Running;
        jobRun.StartedAt = _timeProvider.GetUtcNow();
        jobRun.NotBefore = null;

        await _jobRunRepository.UpdateAsync(jobRun, cancellationToken);
    }
}
