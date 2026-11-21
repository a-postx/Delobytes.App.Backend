namespace Delobytes.App.Backend.Finance.Application.ReadModels;

/// <summary>
/// Ordering of a P&amp;L line page. The newest period first by default: a line list is read as a
/// history, and an unsorted page over a random identifier is useless to a reader.
/// </summary>
public sealed class PnlLineSort
{
    /// <summary>Gets the ordering a request that names none gets.</summary>
    public static PnlLineSort Default { get; } = new PnlLineSort { Key = PnlLineSortKey.Date, Descending = true };

    /// <summary>Gets or sets the key the page is ordered by.</summary>
    public PnlLineSortKey Key { get; set; } = PnlLineSortKey.Date;

    /// <summary>Gets or sets whether the order is descending.</summary>
    public bool Descending { get; set; } = true;
}
