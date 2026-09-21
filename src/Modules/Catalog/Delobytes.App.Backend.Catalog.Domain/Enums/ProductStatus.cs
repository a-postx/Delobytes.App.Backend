namespace Delobytes.App.Backend.Catalog.Domain.Enums;

/// <summary>
/// Product lifecycle status.
/// </summary>
public enum ProductStatus
{
    /// <summary>Active product, visible in the catalog.</summary>
    Active = 1,

    /// <summary>Archived product, hidden but data preserved for reports.</summary>
    Archived = 2,

    /// <summary>Deletion requested, waiting for cross-module validation in Sales.</summary>
    DeletionPending = 3,

    /// <summary>Soft-deleted product — no orders were found for its channel products.</summary>
    Deleted = 4,

    /// <summary>Deletion failed because existing orders reference this product's channel products.</summary>
    DeletionFailed = 5,
}
