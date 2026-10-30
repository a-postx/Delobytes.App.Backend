namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Batch numbering cursor for one orders import run: the next batch index to assign, plus the
/// run-level facts a progress checkpoint needs.
/// </summary>
internal sealed class OrdersImportBatchCursor
{
    public OrdersImportBatchCursor(int nextIndex, DateTimeOffset windowTo, bool isBackfill)
    {
        NextIndex = nextIndex;
        WindowTo = windowTo;
        IsBackfill = isBackfill;
    }

    public int NextIndex { get; private set; }

    public DateTimeOffset WindowTo { get; }

    public bool IsBackfill { get; }

    /// <summary>
    /// Returns the current index and advances the cursor, so the caller never holds a captured
    /// mutable counter.
    /// </summary>
    public int TakeNext()
    {
        int current = NextIndex;
        NextIndex++;
        return current;
    }
}
