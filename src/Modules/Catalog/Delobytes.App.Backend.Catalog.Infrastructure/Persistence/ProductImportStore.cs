using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IProductImportStore"/>. Owns everything the import scenario
/// must not know about: includes, change-tracker lookups of unsaved rows, savepoints and the
/// conflict-translating save path.
/// </summary>
public class ProductImportStore : IProductImportStore
{
    private const string CardSavepointName = "import_card";

    private readonly CatalogDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductImportStore"/> class.
    /// </summary>
    /// <param name="context">Catalog database context.</param>
    public ProductImportStore(CatalogDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public Task<Channel?> FindChannelAsync(Guid channelId, CancellationToken cancellationToken)
    {
        return _context.Channels.FirstOrDefaultAsync(c => c.Id == channelId, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<ChannelProduct?> FindChannelProductWithProductAsync(
        Guid channelId,
        string externalProductId,
        CancellationToken cancellationToken)
    {
        return _context.ChannelProducts
            .Include(cp => cp.Product)
                .ThenInclude(p => p.Barcodes)
            .Include(cp => cp.Product)
                .ThenInclude(p => p.PackingUnits)
            .Include(cp => cp.Product)
                .ThenInclude(p => p.Photos)
            .FirstOrDefaultAsync(
                cp => cp.ChannelId == channelId && cp.ExternalProductId == externalProductId,
                cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Product>> FindProductsByBarcodesAsync(
        IReadOnlyCollection<string> barcodes,
        CancellationToken cancellationToken)
    {
        if (barcodes.Count == 0)
        {
            return Array.Empty<Product>();
        }

        HashSet<Guid> productIds = new HashSet<Guid>();

        List<Guid> persistedProductIds = await _context.ProductBarcodes
            .Where(pb => barcodes.Contains(pb.Value))
            .Select(pb => pb.ProductId)
            .Distinct()
            .ToListAsync(cancellationToken);

        productIds.UnionWith(persistedProductIds);

        // Barcodes added earlier in this same batch are still unsaved and therefore invisible to
        // the query above. Without this pass a second card carrying a barcode already claimed in
        // this batch would create a duplicate row and fail the unique index on barcode value,
        // rolling back every card that came before it.
        foreach (EntityEntry<ProductBarcode> entry in _context.ChangeTracker.Entries<ProductBarcode>())
        {
            if (entry.State == EntityState.Added && barcodes.Contains(entry.Entity.Value))
            {
                productIds.Add(entry.Entity.ProductId);
            }
        }

        if (productIds.Count == 0)
        {
            return Array.Empty<Product>();
        }

        List<Product> productsByBarcode = await _context.Products
            .Where(p => productIds.Contains(p.Id))
            .Include(p => p.Barcodes)
            .Include(p => p.PackingUnits)
            .Include(p => p.Photos)
            .ToListAsync(cancellationToken);

        // Products created earlier in this batch are still unsaved, so they are picked up from the
        // change tracker rather than from the database.
        foreach (Product tracked in _context.Products.Local)
        {
            if (productIds.Contains(tracked.Id) && productsByBarcode.All(p => p.Id != tracked.Id))
            {
                productsByBarcode.Add(tracked);
            }
        }

        return productsByBarcode;
    }

    /// <inheritdoc/>
    public Task<ChannelProduct?> FindChannelProductLinkAsync(
        Guid productId,
        Guid channelId,
        CancellationToken cancellationToken)
    {
        // Queried explicitly rather than via product.ChannelProducts: lazy loading is disabled in
        // this project, so the navigation collection is empty unless the caller included it.
        return _context.ChannelProducts
            .FirstOrDefaultAsync(
                cp => cp.ProductId == productId && cp.ChannelId == channelId,
                cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> IsSkuTakenAsync(string sku, CancellationToken cancellationToken)
    {
        if (await _context.Products.AnyAsync(p => p.Sku == sku, cancellationToken))
        {
            return true;
        }

        // Products created earlier in the same batch are not in the database yet, but they will be
        // at SaveChanges time, where the unique index sees them.
        return _context.Products.Local.Any(p => p.Sku == sku);
    }

    /// <inheritdoc/>
    public void AddProduct(Product product)
    {
        _context.Products.Add(product);
    }

    /// <inheritdoc/>
    public void AddProductBarcode(ProductBarcode barcode)
    {
        _context.ProductBarcodes.Add(barcode);
    }

    /// <inheritdoc/>
    public void AddChannelProduct(ChannelProduct channelProduct)
    {
        _context.ChannelProducts.Add(channelProduct);
    }

    /// <inheritdoc/>
    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task SaveChangesWithConflictTranslationAsync(CancellationToken cancellationToken)
    {
        return _context.SaveChangesWithConflictTranslationAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IProductImportBatchTransaction> BeginBatchAsync(CancellationToken cancellationToken)
    {
        IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        return new BatchTransaction(_context, transaction);
    }

    private sealed class BatchTransaction : IProductImportBatchTransaction
    {
        private readonly CatalogDbContext _context;
        private readonly IDbContextTransaction _transaction;
        private bool? _savepointsSupported;

        public BatchTransaction(CatalogDbContext context, IDbContextTransaction transaction)
        {
            _context = context;
            _transaction = transaction;
        }

        public async Task<IProductImportCardScope> BeginCardAsync(CancellationToken cancellationToken)
        {
            HashSet<object> trackedBefore = _context.ChangeTracker.Entries().Select(e => e.Entity).ToHashSet();
            bool hasSavepoint = await TryCreateSavepointAsync(cancellationToken);

            return new CardScope(_context, _transaction, trackedBefore, hasSavepoint);
        }

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            return _transaction.CommitAsync(cancellationToken);
        }

        public Task RollbackAsync(CancellationToken cancellationToken)
        {
            return _transaction.RollbackAsync(cancellationToken);
        }

        public void Dispose()
        {
            _transaction.Dispose();
        }

        // Cached per batch: Npgsql in production supports savepoints, EF Core InMemory used in
        // unit tests does not. Detected once so the NotSupportedException path isn't retried on
        // every card in the batch.
        private async Task<bool> TryCreateSavepointAsync(CancellationToken cancellationToken)
        {
            if (_savepointsSupported == false)
            {
                return false;
            }

            try
            {
                await _transaction.CreateSavepointAsync(CardSavepointName, cancellationToken);
                _savepointsSupported = true;
                return true;
            }
            catch (NotSupportedException)
            {
                _savepointsSupported = false;
                return false;
            }
        }
    }

    private sealed class CardScope : IProductImportCardScope
    {
        private readonly CatalogDbContext _context;
        private readonly IDbContextTransaction _transaction;
        private readonly HashSet<object> _trackedBefore;
        private readonly bool _hasSavepoint;

        public CardScope(
            CatalogDbContext context,
            IDbContextTransaction transaction,
            HashSet<object> trackedBefore,
            bool hasSavepoint)
        {
            _context = context;
            _transaction = transaction;
            _trackedBefore = trackedBefore;
            _hasSavepoint = hasSavepoint;
        }

        public async Task SaveAsync(CancellationToken cancellationToken)
        {
            await _context.SaveChangesWithConflictTranslationAsync(cancellationToken);

            if (_hasSavepoint)
            {
                await _transaction.ReleaseSavepointAsync(CardSavepointName, cancellationToken);
            }
        }

        public async Task RollbackAsync(CancellationToken cancellationToken)
        {
            // Back to the state before this card, both in PostgreSQL and in the change tracker.
            if (_hasSavepoint)
            {
                await _transaction.RollbackToSavepointAsync(CardSavepointName, cancellationToken);
                await _transaction.ReleaseSavepointAsync(CardSavepointName, cancellationToken);
            }

            // Обязательно: EF не знает об откате и повторит те же INSERT на следующей карточке.
            foreach (EntityEntry entry in _context.ChangeTracker.Entries().ToList())
            {
                if (!_trackedBefore.Contains(entry.Entity))
                {
                    entry.State = EntityState.Detached;
                }
            }

            // Numbering counters advanced by this card were rolled back in the database, but EF
            // still holds the advanced values in memory; without this reload the next card would
            // allocate from a number this card never committed. Sequences first tracked inside the
            // failed card were detached by the loop above, so only committed counters remain here.
            // Counters of cards that already saved stay as the database holds them.
            foreach (EntityEntry<NumberingSequence> sequenceEntry
                in _context.ChangeTracker.Entries<NumberingSequence>().ToList())
            {
                await sequenceEntry.ReloadAsync(cancellationToken);
            }
        }

        public void Dispose()
        {
        }
    }
}
