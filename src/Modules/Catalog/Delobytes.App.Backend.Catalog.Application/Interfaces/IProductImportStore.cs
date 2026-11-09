using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces;

/// <summary>
/// Persistence port of the product batch import. Hides the EF Core specifics the scenario must not
/// know about — queries with includes, change-tracker lookups of rows added earlier in the same
/// batch, savepoints and conflict-translating saves — behind the operations it actually needs.
/// </summary>
public interface IProductImportStore
{
    /// <summary>Finds the channel the batch imports into.</summary>
    public Task<Channel?> FindChannelAsync(Guid channelId, CancellationToken cancellationToken);

    /// <summary>
    /// Finds the link of the given external card to the channel, with its product and the product
    /// collections the import updates (barcodes, packing units, photos).
    /// </summary>
    public Task<ChannelProduct?> FindChannelProductWithProductAsync(
        Guid channelId,
        string externalProductId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns every product whose barcode appears in <paramref name="barcodes"/>, including
    /// products and barcodes added earlier in this batch but not yet saved.
    /// </summary>
    public Task<IReadOnlyList<Product>> FindProductsByBarcodesAsync(
        IReadOnlyCollection<string> barcodes,
        CancellationToken cancellationToken);

    /// <summary>Finds an existing link between the product and the channel.</summary>
    public Task<ChannelProduct?> FindChannelProductLinkAsync(
        Guid productId,
        Guid channelId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reports whether the tenant already holds this internal SKU, including products added earlier
    /// in this batch but not yet saved.
    /// </summary>
    public Task<bool> IsSkuTakenAsync(string sku, CancellationToken cancellationToken);

    /// <summary>Stages a new product.</summary>
    public void AddProduct(Product product);

    /// <summary>Stages a new product barcode.</summary>
    public void AddProductBarcode(ProductBarcode barcode);

    /// <summary>Stages a new channel-product link.</summary>
    public void AddChannelProduct(ChannelProduct channelProduct);

    /// <summary>Persists every pending change.</summary>
    public Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Persists every pending change, translating concurrency and unique-constraint failures into
    /// application errors instead of letting a raw provider exception escape.
    /// </summary>
    public Task SaveChangesWithConflictTranslationAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Opens the batch transaction. Cards are processed one at a time inside it, each with its own
    /// savepoint so one failing card does not roll back the whole batch.
    /// </summary>
    public Task<IProductImportBatchTransaction> BeginBatchAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Batch-level transaction of a product import. Owns commit and rollback; per-card scopes are
/// opened from it.
/// </summary>
public interface IProductImportBatchTransaction : IDisposable
{
    /// <summary>Opens the savepoint scope of a single card.</summary>
    public Task<IProductImportCardScope> BeginCardAsync(CancellationToken cancellationToken);

    /// <summary>Commits the batch.</summary>
    public Task CommitAsync(CancellationToken cancellationToken);

    /// <summary>Rolls the batch back.</summary>
    public Task RollbackAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Per-card unit of work: saves the card's changes and releases its savepoint, or rolls back to
/// that savepoint and detaches everything the failed card staged.
/// </summary>
public interface IProductImportCardScope : IDisposable
{
    /// <summary>Persists the card's changes and releases its savepoint.</summary>
    public Task SaveAsync(CancellationToken cancellationToken);

    /// <summary>Rolls the card back and detaches the entities it staged.</summary>
    public Task RollbackAsync(CancellationToken cancellationToken);
}
