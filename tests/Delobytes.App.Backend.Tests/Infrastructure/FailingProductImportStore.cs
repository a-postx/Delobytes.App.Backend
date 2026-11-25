using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Tests.Infrastructure;

/// <summary>
/// Test double over a real <see cref="IProductImportStore"/> that fails at a chosen point of the
/// batch, so tests can exercise the paths the in-memory provider never fails on: a card that
/// cannot be saved and a batch whose commit fails.
/// </summary>
internal sealed class FailingProductImportStore : IProductImportStore
{
    private readonly IProductImportStore _inner;
    private readonly bool _failCardSave;
    private readonly bool _failCommit;

    internal FailingProductImportStore(IProductImportStore inner, bool failCardSave = false, bool failCommit = false)
    {
        _inner = inner;
        _failCardSave = failCardSave;
        _failCommit = failCommit;
    }

    public Task<Channel?> FindChannelAsync(Guid channelId, CancellationToken cancellationToken)
    {
        return _inner.FindChannelAsync(channelId, cancellationToken);
    }

    public Task<ChannelProduct?> FindChannelProductWithProductAsync(
        Guid channelId,
        string externalProductId,
        CancellationToken cancellationToken)
    {
        return _inner.FindChannelProductWithProductAsync(channelId, externalProductId, cancellationToken);
    }

    public Task<IReadOnlyList<Product>> FindProductsByBarcodesAsync(
        IReadOnlyCollection<string> barcodes,
        CancellationToken cancellationToken)
    {
        return _inner.FindProductsByBarcodesAsync(barcodes, cancellationToken);
    }

    public Task<IReadOnlyList<Product>> FindProductsByChannelBarcodesAsync(
        Guid channelId,
        IReadOnlyCollection<string> barcodes,
        CancellationToken cancellationToken)
    {
        return _inner.FindProductsByChannelBarcodesAsync(channelId, barcodes, cancellationToken);
    }

    public Task<IReadOnlySet<string>> FindTakenCanonicalBarcodesAsync(
        IReadOnlyCollection<string> barcodes,
        Guid excludedProductId,
        CancellationToken cancellationToken)
    {
        return _inner.FindTakenCanonicalBarcodesAsync(barcodes, excludedProductId, cancellationToken);
    }

    public Task<IReadOnlyList<Product>> FindProductsByExternalSkuAsync(
        string externalSku,
        CancellationToken cancellationToken)
    {
        return _inner.FindProductsByExternalSkuAsync(externalSku, cancellationToken);
    }

    public Task<ChannelProduct?> FindChannelProductLinkAsync(
        Guid productId,
        Guid channelId,
        CancellationToken cancellationToken)
    {
        return _inner.FindChannelProductLinkAsync(productId, channelId, cancellationToken);
    }

    public Task<Product?> FindProductBySkuAsync(string sku, CancellationToken cancellationToken)
    {
        return _inner.FindProductBySkuAsync(sku, cancellationToken);
    }

    public void AddProduct(Product product)
    {
        _inner.AddProduct(product);
    }

    public void AddProductBarcode(ProductBarcode barcode)
    {
        _inner.AddProductBarcode(barcode);
    }

    public void AddChannelProduct(ChannelProduct channelProduct)
    {
        _inner.AddChannelProduct(channelProduct);
    }

    public void AddChannelProductBarcode(ChannelProductBarcode barcode)
    {
        _inner.AddChannelProductBarcode(barcode);
    }

    public void RemoveChannelProductBarcode(ChannelProductBarcode barcode)
    {
        _inner.RemoveChannelProductBarcode(barcode);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _inner.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesWithConflictTranslationAsync(CancellationToken cancellationToken)
    {
        return _inner.SaveChangesWithConflictTranslationAsync(cancellationToken);
    }

    public async Task<IProductImportBatchTransaction> BeginBatchAsync(CancellationToken cancellationToken)
    {
        IProductImportBatchTransaction transaction = await _inner.BeginBatchAsync(cancellationToken);

        return new BatchTransaction(transaction, _failCardSave, _failCommit);
    }

    private sealed class BatchTransaction : IProductImportBatchTransaction
    {
        private const string ErrorMessage = "Simulated database failure.";

        private readonly IProductImportBatchTransaction _inner;
        private readonly bool _failCardSave;
        private readonly bool _failCommit;

        public BatchTransaction(IProductImportBatchTransaction inner, bool failCardSave, bool failCommit)
        {
            _inner = inner;
            _failCardSave = failCardSave;
            _failCommit = failCommit;
        }

        public async Task<IProductImportCardScope> BeginCardAsync(CancellationToken cancellationToken)
        {
            IProductImportCardScope scope = await _inner.BeginCardAsync(cancellationToken);

            return new CardScope(scope, _failCardSave);
        }

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            return _failCommit ? throw new InvalidOperationException(ErrorMessage) : _inner.CommitAsync(cancellationToken);
        }

        public Task RollbackAsync(CancellationToken cancellationToken)
        {
            return _inner.RollbackAsync(cancellationToken);
        }

        public void Dispose()
        {
            _inner.Dispose();
        }
    }

    private sealed class CardScope : IProductImportCardScope
    {
        private readonly IProductImportCardScope _inner;
        private readonly bool _failSave;

        public CardScope(IProductImportCardScope inner, bool failSave)
        {
            _inner = inner;
            _failSave = failSave;
        }

        public Task SaveAsync(CancellationToken cancellationToken)
        {
            return _failSave ? throw new InvalidOperationException("Simulated card save failure.") : _inner.SaveAsync(cancellationToken);
        }

        public Task RollbackAsync(CancellationToken cancellationToken)
        {
            return _inner.RollbackAsync(cancellationToken);
        }

        public void Dispose()
        {
            _inner.Dispose();
        }
    }
}
