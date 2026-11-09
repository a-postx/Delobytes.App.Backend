namespace Delobytes.App.Backend.Catalog.Infrastructure.BackgroundServices;

/// <summary>
/// Outcome counters of a single PhotoGC run.
/// </summary>
public class ProductPhotoGcResult
{
    /// <summary>Photo rows deleted together with their blobs.</summary>
    public int DeletedPhotos { get; set; }

    /// <summary>Distinct blob keys deleted from object storage; lower than the row count when two rows share one blob.</summary>
    public int DeletedBlobs { get; set; }

    /// <summary>Tenants whose cleanup failed and was skipped; the next tick retries them.</summary>
    public int FailedTenants { get; set; }
}
