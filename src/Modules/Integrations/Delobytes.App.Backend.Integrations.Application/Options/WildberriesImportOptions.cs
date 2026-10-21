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
}
