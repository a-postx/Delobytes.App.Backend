namespace Delobytes.App.Backend.Integrations.Application.Options;

/// <summary>
/// Tuning of the finance imports.
/// </summary>
public sealed class FinanceImportOptions
{
    /// <summary>Default initial backfill depth in days.</summary>
    public const int DefaultInitialBackfillDays = 180;

    /// <summary>
    /// Default Wildberries initial backfill depth in days. Wildberries serves sales reports no
    /// deeper than 90 days, so asking for more only spends the rate limit on empty periods.
    /// </summary>
    public const int DefaultWbInitialBackfillDays = 90;

    /// <summary>Default Wildberries overlap in days.</summary>
    public const int DefaultWbOverlapDays = 14;

    /// <summary>Default Ozon overlap in days.</summary>
    public const int DefaultOzonOverlapDays = 7;

    /// <summary>Default number of entries per published batch.</summary>
    public const int DefaultBatchSize = 1000;

    /// <summary>Gets or sets the initial backfill depth in days.</summary>
    public int InitialBackfillDays { get; set; } = DefaultInitialBackfillDays;

    /// <summary>Gets or sets the Wildberries initial backfill depth in days.</summary>
    public int WbInitialBackfillDays { get; set; } = DefaultWbInitialBackfillDays;

    /// <summary>Gets or sets the Wildberries overlap in days.</summary>
    public int WbOverlapDays { get; set; } = DefaultWbOverlapDays;

    /// <summary>Gets or sets the Ozon overlap in days.</summary>
    public int OzonOverlapDays { get; set; } = DefaultOzonOverlapDays;

    /// <summary>Gets or sets the number of entries per published batch.</summary>
    public int BatchSize { get; set; } = DefaultBatchSize;
}
