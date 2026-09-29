namespace Delobytes.App.Backend.Contracts.Storage;

/// <summary>
/// Low-level object storage abstraction. Knows nothing about domain entities or key
/// conventions — the caller decides both the key and the target area.
/// </summary>
public interface IObjectStorage
{
    /// <summary>
    /// Uploads (or overwrites) the object identified by <paramref name="key"/> in the given area.
    /// </summary>
    public Task UploadAsync(
        StorageArea area,
        string key,
        Stream content,
        string contentType,
        string? cacheControl,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a single object. No-op if the key does not exist.
    /// </summary>
    public Task DeleteAsync(StorageArea area, string key, CancellationToken cancellationToken);

    /// <summary>Chunks internally: the underlying S3 batch call accepts at most 1000 keys.</summary>
    public Task DeleteManyAsync(StorageArea area, IReadOnlyCollection<string> keys, CancellationToken cancellationToken);

    /// <summary>
    /// Checks whether an object with the given key exists in the area.
    /// </summary>
    public Task<bool> ExistsAsync(StorageArea area, string key, CancellationToken cancellationToken);

    /// <summary>Builds a stable public URL. Throws when the area is not configured for public access.</summary>
    public string GetPublicUrl(StorageArea area, string key);
}
