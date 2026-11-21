namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>
/// Dimension the P&amp;L summary is grouped by. One axis per request: the row carries the identifier
/// of the chosen dimension and leaves the other one empty, so a consumer never has to guess which
/// identifier it is looking at.
/// </summary>
public enum PnlSummaryGroupBy
{
    /// <summary>One row per catalogue product. Lines without a resolved product form their own group.</summary>
    Product = 0,

    /// <summary>
    /// One row per sales channel. The channel becomes the group instead of a filter, which is how a
    /// client answers "which channel earns": the filter would only answer "what is inside a channel".
    /// </summary>
    Channel = 1,

    /// <summary>
    /// No dimension: all lines of a period fold into one row. This is the only way to get a trend
    /// over the whole business, because the per-group percentages cannot be averaged, and a
    /// <see cref="Product"/> grouping with a total granularity still yields one row per product.
    /// The row carries neither a product nor a channel identifier.
    /// </summary>
    None = 2,
}
