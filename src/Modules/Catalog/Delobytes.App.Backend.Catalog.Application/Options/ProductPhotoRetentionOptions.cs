namespace Delobytes.App.Backend.Catalog.Application.Options;

/// <summary>
/// Retention policy for photo blobs whose row was superseded or undeclared. The grace period
/// exists so a public URL already handed to a CDN or a customer keeps resolving after the
/// photo stops being the current version of its slot.
/// </summary>
public class ProductPhotoRetentionOptions
{
    /// <summary>Days a <c>Removed</c> row and its blob are kept before the PhotoGC removes them.</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>How often the PhotoGC runs, in hours.</summary>
    public int IntervalHours { get; set; } = 24;
}
