namespace Delobytes.App.Backend.Integrations.Application.Options;

/// <summary>
/// Configuration options for Ozon orders import (FBO; FBS is reserved).
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
    /// Gets or sets a value indicating whether Ozon FBS orders import is enabled. FBS is not
    /// implemented yet; this flag only controls whether <see cref="Domain.Enums.JobType.OrdersImportFbs"/>
    /// resolves to a fetcher at all.
    /// </summary>
    public bool EnableFbs { get; set; } = false;
}
