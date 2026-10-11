using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Versioned assembly output rate for a specific product.
/// Captures how many units of this product one worker produces per day.
/// Adding a new record (POST) supersedes the version currently in force and appends a new one;
/// an existing version can also be corrected in place (PUT) while it is still active, or withdrawn
/// by the user (DELETE), which dates the moment it stops being in force.
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
    /// Lifecycle state of this version. <c>Superseded</c> marks a version a newer one replaced;
    /// <c>Removed</c> marks one the user withdrew, and only for those is <c>RemovedFrom</c> set.
    /// The two were a single boolean before, which is why the date below carries the meaning the
    /// flag could not.
    /// </summary>
    public ProductWorkRateStatus Status { get; set; }

    /// <summary>
    /// First date on which this version is no longer in force; null for every version that was not
    /// removed. Set once, when the user withdraws the rate, and never modified afterwards. The rate
    /// keeps resolving for every earlier date, so historical cost calculations are unaffected.
    /// Note that removal closes this version only: an earlier version that was never superseded
    /// still resolves from its own ValidFrom, so removing the latest version does not by itself
    /// create a gap in which the product has no rate. To produce such a gap, the preceding version
    /// must be removed as well, or a new version added.
    /// </summary>
    public DateOnly? RemovedFrom { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set on supersede, on removal and on an in-place correction; never implicit.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Optimistic concurrency token mapped to the PostgreSQL xmin system column.</summary>
    public uint RowVersion { get; set; }

    public Product Product { get; set; } = default!;

    public WorkRate WorkRate { get; set; } = default!;
}
