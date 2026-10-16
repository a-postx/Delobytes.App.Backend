using Delobytes.App.Backend.Integrations.Domain.Entities;

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
}
