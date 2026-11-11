using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces;

/// <summary>
/// Domain-aware photo operations: content-addressed key convention, WebP validation, reconciliation.
/// Raw byte transfer is delegated to <c>IObjectStorage</c>.
/// </summary>
public interface IProductPhotoService
{
    /// <summary>
    /// Reconciles the photo set of one channel's card on the product: downloads and hashes every
    /// declared source, uploads content that is not stored yet, supersedes rows whose bytes changed
    /// and marks rows the card no longer declares as <see cref="ProductPhotoStatus.Removed"/>.
    /// Adds and mutates rows on the tracked product but does NOT call SaveChanges — the caller
    /// owns the transaction boundary.
    /// </summary>
    /// <param name="product">Tracked product that owns the photos.</param>
    /// <param name="channelId">Channel whose card declares the set; other channels' rows are left alone.</param>
    /// <param name="photos">Photo sources reported by the marketplace; may be empty, which removes every live photo of the channel.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Per-run counters describing what happened to each source.</returns>
    public Task<ProductPhotoImportResult> ImportPhotosAsync(
        Product product,
        Guid channelId,
        IReadOnlyList<MarketplacePhotoSource> photos,
        CancellationToken cancellationToken);

    /// <summary>Deletes the given photos' blobs from storage. Does not touch the database.</summary>
    /// <param name="photos">Photos whose blobs must be removed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task DeletePhotosAsync(IReadOnlyList<ProductPhoto> photos, CancellationToken cancellationToken);

    /// <summary>Builds a public URL. No network call — safe to use inside mapping loops.</summary>
    /// <param name="photo">Photo to build the URL for.</param>
    /// <returns>Publicly reachable URL of the stored blob.</returns>
    public string GetPublicUrl(ProductPhoto photo);
}
