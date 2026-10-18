using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Repository interface for JobTypeConfiguration (system-wide reference table, not tenant-scoped).
/// </summary>
public interface IJobTypeConfigurationRepository
{
    /// <summary>
    /// Finds the configuration row for the given job type, or null if it has not been seeded.
    /// </summary>
    public Task<JobTypeConfiguration?> GetByTypeAsync(JobType jobType, CancellationToken cancellationToken);
}
