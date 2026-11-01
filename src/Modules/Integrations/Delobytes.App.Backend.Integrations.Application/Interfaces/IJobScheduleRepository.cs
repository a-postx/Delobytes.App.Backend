using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Repository interface for JobSchedule (tenant-scoped cron schedule per connection/job type).
/// </summary>
public interface IJobScheduleRepository
{
    /// <summary>
    /// Returns every enabled schedule (across all tenants) whose NextRunAt is due at or before
    /// <paramref name="now"/>, for the scheduler to fire.
    /// </summary>
    public Task<List<JobSchedule>> GetDueSchedulesAsync(DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Persists changes made to an existing schedule (LastRunAt/NextRunAt advanced by the scheduler).
    /// </summary>
    public Task UpdateAsync(JobSchedule schedule, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the job types that already have a schedule on <paramref name="connectionId"/>, so a
    /// caller creating schedules can skip the pairs that exist instead of provoking a unique
    /// constraint violation.
    /// </summary>
    public Task<List<JobType>> GetScheduledJobTypesAsync(Guid connectionId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds the given schedules for a connection, skipping the (ConnectionId, JobType) pairs that
    /// already have one, and persists them in a single save.
    /// </summary>
    /// <param name="connectionId">Connection the schedules belong to.</param>
    /// <param name="schedules">Candidate schedules; their ConnectionId is expected to match.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of schedules actually created.</returns>
    public Task<int> AddMissingAsync(
        Guid connectionId,
        IReadOnlyList<JobSchedule> schedules,
        CancellationToken cancellationToken);
}
