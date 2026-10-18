using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Domain.Entities;

/// <summary>
/// System-wide reference data describing operational limits per job type. Not tenant-scoped:
/// every tenant shares the same timeout/batch size for a given <see cref="JobType"/>.
/// </summary>
public class JobTypeConfiguration
{
    /// <summary>
    /// Gets or sets the job type (primary key).
    /// </summary>
    public JobType JobType { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of minutes a job of this type may stay Running before
    /// WatchdogHostedService transitions it to Failed.
    /// </summary>
    public int TimeoutMinutes { get; set; }

    /// <summary>
    /// Gets or sets the page/batch size orchestration consumers should request for this job type.
    /// </summary>
    public int BatchSize { get; set; }
}
