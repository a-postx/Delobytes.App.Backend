using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces;

/// <summary>
/// Domain-aware photo operations: content-addressed key convention, WebP validation, reconciliation.
/// Raw byte transfer is delegated to <c>IObjectStorage</c>.
/// </summary>
public interface IProductPhotoService
{
    /// <summary>
    /// Reconciles the product's own gallery: downloads and hashes every declared source, uploads
    /// content that is not stored yet, supersedes rows whose bytes changed and marks rows the card
    /// no longer declares as <see cref="Domain.Enums.ProductPhotoStatus.Removed"/>.
    /// Adds and mutates rows on the tracked product but does NOT call SaveChanges — the caller
    /// owns the transaction boundary.
    /// </summary>
    /// <param name="product">Tracked product that owns the gallery.</param>
    /// <param name="photos">Photo sources reported by the marketplace; may be empty, which removes every live canonical photo.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Per-run counters describing what happened to each source.</returns>
    public Task<ProductPhotoImportResult> ImportCanonicalPhotosAsync(
        Product product,
        IReadOnlyList<MarketplacePhotoSource> photos,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reconciles the gallery of one channel's card: a full replacement of that card's photo set.
    /// Other channels' cards and the product's own gallery are never touched.
    /// Adds and mutates rows on the tracked link but does NOT call SaveChanges — the caller owns
    /// the transaction boundary.
    /// </summary>
    /// <param name="channelProduct">Tracked link whose card declares the set.</param>
    /// <param name="photos">Photo sources reported by the marketplace; may be empty, which removes every live photo of the card.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Per-run counters describing what happened to each source.</returns>
    public Task<ProductPhotoImportResult> SyncChannelPhotosAsync(
        ChannelProduct channelProduct,
        IReadOnlyList<MarketplacePhotoSource> photos,
        CancellationToken cancellationToken);

    /// <summary>Deletes the given blobs from storage. Does not touch the database.</summary>
    /// <param name="storageKeys">Object keys whose blobs must be removed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task DeletePhotosAsync(IReadOnlyList<string> storageKeys, CancellationToken cancellationToken);

    /// <summary>Builds a public URL. No network call — safe to use inside mapping loops.</summary>
    /// <param name="storageKey">Object key of the stored blob.</param>
    /// <returns>Publicly reachable URL of the stored blob.</returns>
    public string GetPublicUrl(string storageKey);
}
