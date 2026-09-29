namespace Delobytes.App.Backend.Contracts.Storage;

/// <summary>
/// Logical storage destinations. Each area maps to its own bucket and access mode
/// in configuration, so adding a new area never changes IObjectStorage itself.
/// </summary>
public enum StorageArea
{
    ProductPhotos = 0
}
