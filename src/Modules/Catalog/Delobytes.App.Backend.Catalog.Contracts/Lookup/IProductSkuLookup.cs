namespace Delobytes.App.Backend.Catalog.Contracts.Lookup;

/// <summary>
/// Read contract of the catalogue for other modules: resolves the SKU of products by identifier.
/// </summary>
public interface IProductSkuLookup
{
    /// <summary>
    /// Returns the SKU of the requested products of the current tenant. An identifier the tenant
    /// does not hold is absent from the result: a missing label is not an error, because the
    /// caller holds its own snapshot of identifiers and only needs a label for the rows it shows.
    /// </summary>
    /// <param name="productIds">Product identifiers to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The SKU of each resolved product, keyed by its identifier.</returns>
    public Task<IReadOnlyDictionary<Guid, string>> GetSkusAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken);
}
