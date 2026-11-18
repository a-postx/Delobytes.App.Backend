namespace Delobytes.App.Backend.Integrations.Application.Finance;

/// <summary>
/// Splits a finance page into messages small enough for the bounded host. Pure.
/// </summary>
public static class FinanceBatchSplitter
{
    /// <summary>
    /// Splits <paramref name="items"/> into chunks of at most <paramref name="batchSize"/>.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="items">Items to split.</param>
    /// <param name="batchSize">Maximum chunk size; a non-positive value means 1000.</param>
    /// <returns>The chunks, in input order.</returns>
    public static IReadOnlyList<IReadOnlyList<T>> Split<T>(IReadOnlyList<T> items, int batchSize)
    {
        ArgumentNullException.ThrowIfNull(items);

        int size = batchSize > 0 ? batchSize : 1000;

        List<IReadOnlyList<T>> batches = new();

        for (int offset = 0; offset < items.Count; offset += size)
        {
            List<T> batch = new(Math.Min(size, items.Count - offset));

            for (int index = offset; index < Math.Min(offset + size, items.Count); index++)
            {
                batch.Add(items[index]);
            }

            batches.Add(batch);
        }

        return batches;
    }
}
