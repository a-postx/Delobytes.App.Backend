using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of IJobScheduleRepository.
/// </summary>
public class JobScheduleRepository : IJobScheduleRepository
{
    private readonly IntegrationsDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobScheduleRepository"/> class.
    /// </summary>
    /// <param name="context">Database context.</param>
    public JobScheduleRepository(IntegrationsDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public Task<List<JobSchedule>> GetDueSchedulesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        // The scheduler fans out across every tenant in a single tick, so it runs with no ambient
        // tenant set; the query filter would otherwise hide every row.
        return _context.JobSchedules
            .IgnoreQueryFilters()
            .Where(s => s.IsEnabled && s.NextRunAt != null && s.NextRunAt <= now)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task UpdateAsync(JobSchedule schedule, CancellationToken cancellationToken)
    {
        _context.JobSchedules.Update(schedule);
        return _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<JobType>> GetScheduledJobTypesAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        return await _context.JobSchedules
            .Where(s => s.ConnectionId == connectionId)
            .Select(s => s.JobType)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int> AddMissingAsync(
        Guid connectionId,
        IReadOnlyList<JobSchedule> schedules,
        CancellationToken cancellationToken)
    {
        List<JobType> existing = await GetScheduledJobTypesAsync(connectionId, cancellationToken);
        int createdCount = 0;

        foreach (JobSchedule schedule in schedules)
        {
            if (existing.Contains(schedule.JobType))
            {
                continue;
            }

            _context.JobSchedules.Add(schedule);
            createdCount++;
        }

        if (createdCount == 0)
        {
            return 0;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return createdCount;
    }
}
