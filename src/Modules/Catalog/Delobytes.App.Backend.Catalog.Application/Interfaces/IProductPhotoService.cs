using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces;

/// <summary>
/// Domain-aware photo operations: key convention, WebP validation, idempotency.
/// Raw byte transfer is delegated to <c>IObjectStorage</c>.
/// </summary>
public interface IProductPhotoService
{
    /// <summary>
    /// Downloads, validates and uploads the given photo sources, skipping ones already
    /// imported (matched by <see cref="MarketplacePhotoSource.ExternalId"/>). Adds new
    /// <see cref="ProductPhoto"/> rows to the tracked product but does NOT call SaveChanges
    /// — the caller owns the transaction boundary.
    /// </summary>
    /// <param name="product">Tracked product that owns the photos.</param>
    /// <param name="photos">Photo sources reported by the marketplace.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Per-run counters describing what happened to each source.</returns>
    Task<ProductPhotoImportResult> ImportPhotosAsync(
        Product product,
        IReadOnlyList<MarketplacePhotoSource> photos,
        CancellationToken cancellationToken);

    /// <summary>Deletes the given photos' blobs from storage. Does not touch the database.</summary>
    /// <param name="photos">Photos whose blobs must be removed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeletePhotosAsync(IReadOnlyList<ProductPhoto> photos, CancellationToken cancellationToken);

    /// <summary>Builds a public URL. No network call — safe to use inside mapping loops.</summary>
    /// <param name="photo">Photo to build the URL for.</param>
    /// <returns>Publicly reachable URL of the stored blob.</returns>
    string GetPublicUrl(ProductPhoto photo);
}
