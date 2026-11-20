using Delobytes.App.Backend.Integrations.Domain.Enums;

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
    /// Cron expression (NCrontab, 5 fields) used to seed the initial orders and products import
    /// schedules for a connection that doesn't have one yet. Default: every hour.
    /// </summary>
    public string DefaultOrdersImportCronExpression { get; set; } = "0 * * * *";

    /// <summary>
    /// Cron expression used to seed the Wildberries finance report schedule. Default: daily at
    /// 08:00 in the tenant's time zone - the weekly detailed report and its accruals do not change
    /// fast enough to justify the rate limit a more frequent poll costs.
    /// </summary>
    public string DefaultWbFinanceImportCronExpression { get; set; } = "0 8 * * *";

    /// <summary>
    /// Cron expression used to seed the Ozon finance accruals schedule. Default: daily at 09:00 in
    /// the tenant's time zone, staggered against the Wildberries finance job so both do not compete
    /// for the same tenant slot.
    /// </summary>
    public string DefaultOzonFinanceImportCronExpression { get; set; } = "0 9 * * *";

    /// <summary>
    /// Returns the default cron expression for the given job type: hourly for the operational
    /// imports (orders, products) and daily for the finance ones.
    /// </summary>
    /// <param name="jobType">Job type a schedule is being created for.</param>
    /// <returns>Cron expression to seed the schedule with.</returns>
    public string ResolveDefaultCronExpression(JobType jobType)
    {
        return jobType switch
        {
            JobType.WbFinanceReportImport => DefaultWbFinanceImportCronExpression,
            JobType.OzonFinanceAccrualsImport => DefaultOzonFinanceImportCronExpression,
            _ => DefaultOrdersImportCronExpression,
        };
    }
}
