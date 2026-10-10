namespace Delobytes.App.Backend.Integrations.Application.DTOs;

/// <summary>
/// Position within a paged orders query. Every field is optional because the three channels
/// page differently; a client reads and advances only the field its own API uses.
/// </summary>
public class OrdersPageCursor
{
    /// <summary>
    /// Gets or sets the frozen report snapshot time requested by the caller.
    /// Wildberries only: the offset is scoped to one snapshot, so a caller that drops it
    /// gets a report rebuilt from live data and may see rows shift between pages.
    /// </summary>
    public DateTimeOffset? SnapshotTime { get; set; }

    /// <summary>
    /// Gets or sets the number of records already consumed. Used by Wildberries and Ozon.
    /// </summary>
    public int Offset { get; set; }

    /// <summary>
    /// Gets or sets the opaque continuation token. Used by Yandex Market.
    /// </summary>
    public string? PageToken { get; set; }
}
