namespace Delobytes.App.Backend.Integrations.Application.Options;

/// <summary>
/// Operational configuration for the background job scheduler/worker.
/// </summary>
public class BackgroundJobsOptions
{
    /// <summary>
    /// How often SchedulerHostedService checks for due schedules, in seconds.
    /// </summary>
    public int SchedulerTickIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// How often WorkerHostedService checks for Pending jobs to dispatch, in seconds.
    /// </summary>
    public int WorkerTickIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Cron expression (NCrontab, 5 fields) used to seed the initial OrdersImport schedule for a
    /// connection that doesn't have one yet. Default: every hour.
    /// </summary>
    public string DefaultOrdersImportCronExpression { get; set; } = "0 * * * *";
}
