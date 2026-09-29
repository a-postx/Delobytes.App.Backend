namespace Delobytes.App.Backend.Catalog.Infrastructure.Storage;

/// <summary>
/// Object key convention for the <c>ProductPhotos</c> storage area.
/// Kept separate from <see cref="ProductPhotoService"/> so the convention can be asserted
/// in tests and reused by any future code that needs to locate a blob.
/// </summary>
internal static class ProductPhotoKeys
{
    /// <summary>Folder prefix inside the bucket that holds every product photo.</summary>
    internal const string Prefix = "product-photos";

    /// <summary>Extension of every stored photo; the only accepted input format is WebP.</summary>
    internal const string Extension = "webp";

    /// <summary>
    /// Builds <c>product-photos/{tenantId:N}/{productId:N}/{photoId:N}.webp</c>.
    /// The photo id is part of the key, so a retry keeps the same key and overwrites
    /// the same object instead of leaving an orphan behind.
    /// </summary>
    /// <param name="tenantId">Owning tenant; isolates one tenant's objects from another's.</param>
    /// <param name="productId">Product the photo belongs to.</param>
    /// <param name="photoId">Row identifier, stable across retries of the same row.</param>
    /// <returns>Storage key relative to the bucket root.</returns>
    internal static string Build(Guid tenantId, Guid productId, Guid photoId)
    {
        return $"{Prefix}/{tenantId:N}/{productId:N}/{photoId:N}.{Extension}";
    }
}
