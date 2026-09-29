namespace Delobytes.App.Backend.Catalog.Application.Interfaces;

/// <summary>
/// Outcome counters of a single <see cref="IProductPhotoService.ImportPhotosAsync"/> run.
/// Every source is accounted for exactly once: Imported + Skipped + Failed equals the
/// number of sources passed in.
/// </summary>
public class ProductPhotoImportResult
{
    /// <summary>Sources downloaded, validated, uploaded and added as new rows.</summary>
    public int Imported { get; set; }

    /// <summary>Sources that already existed with <c>Uploaded</c> status and were left untouched.</summary>
    public int Skipped { get; set; }

    /// <summary>Sources that could not be imported; each one is logged with its reason.</summary>
    public int Failed { get; set; }
}
