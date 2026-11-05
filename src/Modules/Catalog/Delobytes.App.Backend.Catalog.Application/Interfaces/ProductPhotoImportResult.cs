namespace Delobytes.App.Backend.Catalog.Application.Interfaces;

/// <summary>
/// Outcome counters of a single <see cref="IProductPhotoService.ImportPhotosAsync"/> run.
/// Imported + Replaced + Skipped + Failed equals the number of sources passed in;
/// <see cref="Removed"/> is counted separately and is not part of that sum.
/// </summary>
public class ProductPhotoImportResult
{
    /// <summary>New slot in this channel: blob uploaded and a new <c>Uploaded</c> row added.</summary>
    public int Imported { get; set; }

    /// <summary>Slot existed in this channel with different bytes: a new row was added and the prior one superseded.</summary>
    public int Replaced { get; set; }

    /// <summary>Hash matched the live row, a legacy row was adopted, or the source was a duplicate in this run.</summary>
    public int Skipped { get; set; }

    /// <summary>Download, validation or upload failure; each one is logged with its reason.</summary>
    public int Failed { get; set; }

    /// <summary>Live rows of this channel no longer declared by the card.</summary>
    public int Removed { get; set; }
}
