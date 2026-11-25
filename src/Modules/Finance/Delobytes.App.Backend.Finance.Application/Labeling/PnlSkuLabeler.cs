using Delobytes.App.Backend.Catalog.Contracts.Lookup;
using Delobytes.App.Backend.Finance.Application.ReadModels;

namespace Delobytes.App.Backend.Finance.Application.Labeling;

/// <summary>
/// Applies the current SKU of each linked catalogue product to a set of P&amp;L rows. Shared by the
/// P&amp;L queries so the label costs one lookup per response, requested after any ranking slice.
/// </summary>
public static class PnlSkuLabeler
{
    public static async Task ApplyAsync<T>(
        IProductSkuLookup lookup,
        IReadOnlyList<T> rows,
        CancellationToken cancellationToken)
        where T : IPnlSkuLabelTarget
    {
        Guid[] productIds = rows
            .Where(row => row.ProductId.HasValue)
            .Select(row => row.ProductId!.Value)
            .Distinct()
            .ToArray();

        if (productIds.Length == 0)
        {
            return;
        }

        IReadOnlyDictionary<Guid, string> skus = await lookup.GetSkusAsync(productIds, cancellationToken);

        foreach (T row in rows)
        {
            if (row.ProductId.HasValue
                && skus.TryGetValue(row.ProductId.Value, out string? sku)
                && sku is not null)
            {
                row.Sku = sku;
            }
        }
    }
}
