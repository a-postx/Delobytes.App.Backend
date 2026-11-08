using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Application.Options;

/// <summary>
/// Configuration options for Wildberries import (product cards and orders).
/// </summary>
public class WildberriesImportOptions
{
    /// <summary>
    /// Default for <see cref="MaxOrdersPerResponse"/>; also the fallback used when configuration
    /// supplies a non-positive value.
    /// </summary>
    public const int DefaultMaxOrdersPerResponse = 80000;

    /// <summary>
    /// Default for <see cref="BackfillRunBudgetSeconds"/>.
    /// </summary>
    public const int DefaultBackfillRunBudgetSeconds = 1200;

    public const int DefaultMaxInProcessWaitSeconds = 300;

    /// <summary>
    /// Hard bound on the number of HTTP requests one orders run may issue.
    /// </summary>
    public const int DefaultMaxRequestsPerRun = 1000;

    public const int DefaultSafetyOverlapMinutes = 30;

    public const int DefaultBackfillWindowDays = 180;

    public static readonly TimeSpan DefaultSafetyOverlap = TimeSpan.FromMinutes(DefaultSafetyOverlapMinutes);

    public static readonly TimeSpan DefaultBackfillWindow = TimeSpan.FromDays(DefaultBackfillWindowDays);

    /// <summary>
    /// Keep the run budget plus this wait below the 30-minute OrdersImport watchdog timeout.
    /// </summary>
    public int MaxInProcessWaitSeconds { get; set; } = DefaultMaxInProcessWaitSeconds;

    /// <summary>
    /// Number of product cards to retrieve per API call. Must be between 1 and 100.
    /// </summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Maximum number of order rows the <c>/api/v1/supplier/orders</c> endpoint returns for one
    /// response when the requested window holds more rows than it can emit. A response of exactly
    /// this size is a partial subset of the window and must be narrowed and re-requested.
    /// </summary>
    public int MaxOrdersPerResponse { get; set; } = DefaultMaxOrdersPerResponse;

    /// <summary>
    /// Narrowest window the orders importer is allowed to request when narrowing a window that
    /// exceeded <see cref="MaxOrdersPerResponse"/>. Below this width the endpoint cannot be asked
    /// for less data, so a still-partial response at this width is a hard failure.
    /// </summary>
    public TimeSpan MinOrdersWindow { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Wall-clock fetch budget for both backfill and delta. The budget plus
    /// MaxInProcessWaitSeconds must stay below the 30-minute OrdersImport watchdog timeout.
    /// </summary>
    public int BackfillRunBudgetSeconds { get; set; } = DefaultBackfillRunBudgetSeconds;

    /// <summary>
    /// Resolves the effective orders-import settings. This is the only place that knows which
    /// configuration value is a sentinel and which is a deliberate zero.
    /// </summary>
    public OrdersImportTuning ToTuning(JobType jobType)
    {
        return new OrdersImportTuning
        {
            RunBudget = TimeSpan.FromSeconds(
                BackfillRunBudgetSeconds < 1 ? DefaultBackfillRunBudgetSeconds : BackfillRunBudgetSeconds),
            MaxInProcessWait = TimeSpan.FromSeconds(
                MaxInProcessWaitSeconds < 0 ? DefaultMaxInProcessWaitSeconds : MaxInProcessWaitSeconds),
            MaxRequestsPerRun = DefaultMaxRequestsPerRun,
            BatchSize = OrdersImportTuning.DefaultBatchSize,
            SafetyOverlap = DefaultSafetyOverlap,
            BackfillWindow = DefaultBackfillWindow,
            MinOrdersWindow = MinOrdersWindow > TimeSpan.Zero ? MinOrdersWindow : TimeSpan.FromHours(1),
            PageSize = null,
            MethodName = OrdersRateLimiterMethods.WildberriesSupplierOrders,
        };
    }
}
