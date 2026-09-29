namespace Delobytes.App.Backend.Catalog.Domain.Enums;

/// <summary>
/// Lifecycle state of a product photo row.
/// </summary>
public enum ProductPhotoStatus
{
    /// <summary>Row created but the blob has not been uploaded yet.</summary>
    Pending = 0,

    /// <summary>Blob uploaded and available.</summary>
    Uploaded = 1,

    /// <summary>Download or upload failed; see ErrorMessage.</summary>
    Failed = 2
}
