using Delobytes.App.Backend.Catalog.Contracts.Lookup;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Lookup;

/// <summary>
/// Resolves product SKUs for other modules. The tenant query filter of
/// <see cref="CatalogDbContext"/> applies to every batch, so an identifier of another tenant
/// resolves to nothing instead of leaking a SKU across tenants.
/// </summary>
public sealed class ProductSkuLookup : IProductSkuLookup
{
    // One array parameter per batch: large enough to keep the round trips few, small enough to
    // keep the statement's parameter and result buffers predictable.
    private const int BatchSize = 5000;

    private readonly CatalogDbContext _context;

    public ProductSkuLookup(CatalogDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetSkusAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, string> skus = new(productIds.Count);

        if (productIds.Count == 0)
        {
            return skus;
        }

        // A projection rather than the list query: the lookup labels another module's report and
        // needs no barcodes, photos or channel links. Contains becomes a single ANY(...) predicate.
        foreach (Guid[] batch in productIds.Distinct().Chunk(BatchSize))
        {
            Dictionary<Guid, string> batchSkus = await _context.Products
                .AsNoTracking()
                .Where(product => batch.Contains(product.Id))
                .ToDictionaryAsync(product => product.Id, product => product.Sku, cancellationToken);

            foreach (KeyValuePair<Guid, string> pair in batchSkus)
            {
                skus[pair.Key] = pair.Value;
            }
        }

        return skus;
    }
}
