using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Versioned assembly output rate for a specific product.
/// Captures how many units of this product one worker produces per day.
/// Adding a new record (POST) deactivates the previously active version and appends a new one;
/// an existing version can also be corrected in place (PUT) while it is still active.
/// Neither path overwrites a version's ValidFrom silently, preserving accuracy of historical
/// cost calculations.
/// </summary>
public class ProductWorkRate : ITenantScoped, IRowVersionedEntity
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    /// <summary>The work rate (employee daily wage) applied to this product.</summary>
    public Guid WorkRateId { get; set; }

    /// <summary>Number of finished units one worker assembles per day for this product.</summary>
    public int AssemblyRatePerDay { get; set; }

    /// <summary>Date from which this rate version becomes effective.</summary>
    public DateOnly ValidFrom { get; set; }

    /// <summary>
    /// False in two distinct cases, both treated as "inactive" by the UI: the version was
    /// <c>Superseded</c> by a newer one appended through POST, or it was <c>Removed</c> via
    /// soft-delete (DELETE). GetEffectiveAtAsync deliberately ignores this flag either way: a
    /// version superseded or deleted later was still the only effective one on an earlier date.
    /// </summary>
    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set on deactivation (superseded/removed) and on an in-place correction; never implicit.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Optimistic concurrency token mapped to the PostgreSQL xmin system column.</summary>
    public uint RowVersion { get; set; }

    public Product Product { get; set; } = default!;

    public WorkRate WorkRate { get; set; } = default!;
}
