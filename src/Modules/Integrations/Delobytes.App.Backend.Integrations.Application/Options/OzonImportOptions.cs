using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Application.Options;

/// <summary>
/// Configuration options for Ozon orders import (both FBO and FBS).
/// </summary>
public class OzonImportOptions
{
    public const int DefaultPageSize = 100;

    public const int DefaultBackfillRunBudgetSeconds = 1200;

    public const int DefaultMaxInProcessWaitSeconds = 300;

    public const int DefaultMaxRequestsPerRun = 1000;

    public const int DefaultBackfillWindowDays = 180;

    public const int DefaultSafetyOverlapMinutes = 30;

    public const int DefaultMinimumRequestIntervalSeconds = 15;

    /// <summary>
    /// Number of postings requested per <c>posting/fbo/list</c> page. Must be between 1 and 100.
    /// </summary>
    public int PageSize { get; set; } = DefaultPageSize;

    /// <summary>
    /// Wall-clock fetch budget for both backfill and delta. The budget plus
    /// <see cref="MaxInProcessWaitSeconds"/> must stay below the 30-minute OrdersImport watchdog
    /// timeout.
    /// </summary>
    public int BackfillRunBudgetSeconds { get; set; } = DefaultBackfillRunBudgetSeconds;

    /// <summary>
    /// Keep the run budget plus this wait below the 30-minute OrdersImport watchdog timeout.
    /// </summary>
    public int MaxInProcessWaitSeconds { get; set; } = DefaultMaxInProcessWaitSeconds;

    /// <summary>
    /// Hard bound on the number of HTTP requests one run may issue.
    /// </summary>
    public int MaxRequestsPerRun { get; set; } = DefaultMaxRequestsPerRun;

    /// <summary>
    /// Width of the backfill scope, in days back from today, for a fresh connection.
    /// </summary>
    public int BackfillWindowDays { get; set; } = DefaultBackfillWindowDays;

    /// <summary>
    /// Overlap subtracted from the last known watermark when resolving a delta run's window
    /// start, to tolerate clock skew and late-arriving status changes.
    /// </summary>
    public int SafetyOverlapMinutes { get; set; } = DefaultSafetyOverlapMinutes;

    /// <summary>
    /// Minimum interval between consecutive Ozon API requests, in seconds. Enforced globally per
    /// API key across all job types (FBO and FBS) to spread backfill load over time. Default:
    /// 15 seconds. Set to 0 to disable.
    /// </summary>
    public int MinimumRequestIntervalSeconds { get; set; } = DefaultMinimumRequestIntervalSeconds;

    /// <summary>
    /// Resolves the effective minimum interval; a negative value falls back to the default.
    /// </summary>
    public TimeSpan ResolveMinimumRequestInterval()
    {
        int seconds = MinimumRequestIntervalSeconds < 0
            ? DefaultMinimumRequestIntervalSeconds
            : MinimumRequestIntervalSeconds;

        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Resolves the effective orders-import settings for one Ozon job type. This is the only place
    /// that knows which configuration value is a sentinel and which is a deliberate zero.
    /// </summary>
    public OrdersImportTuning ToTuning(JobType jobType)
    {
        string methodName = jobType switch
        {
            JobType.OrdersImportFbo => OrdersRateLimiterMethods.OzonPostingFboList,
            JobType.OrdersImportFbs => OrdersRateLimiterMethods.OzonPostingFbsList,
            _ => throw new ArgumentOutOfRangeException(
                nameof(jobType),
                jobType,
                "Ozon orders import supports only the FBO and FBS job types."),
        };

        return new OrdersImportTuning
        {
            RunBudget = TimeSpan.FromSeconds(
                BackfillRunBudgetSeconds < 1 ? DefaultBackfillRunBudgetSeconds : BackfillRunBudgetSeconds),
            MaxInProcessWait = TimeSpan.FromSeconds(
                MaxInProcessWaitSeconds < 0 ? DefaultMaxInProcessWaitSeconds : MaxInProcessWaitSeconds),
            MaxRequestsPerRun = MaxRequestsPerRun > 0 ? MaxRequestsPerRun : DefaultMaxRequestsPerRun,
            BatchSize = OrdersImportTuning.DefaultBatchSize,
            SafetyOverlap = TimeSpan.FromMinutes(
                SafetyOverlapMinutes < 0 ? DefaultSafetyOverlapMinutes : SafetyOverlapMinutes),
            BackfillWindow = TimeSpan.FromDays(
                BackfillWindowDays < 1 ? DefaultBackfillWindowDays : BackfillWindowDays),
            MinOrdersWindow = TimeSpan.Zero,
            PageSize = PageSize > 0 ? PageSize : DefaultPageSize,
            MethodName = methodName,
        };
    }
}
