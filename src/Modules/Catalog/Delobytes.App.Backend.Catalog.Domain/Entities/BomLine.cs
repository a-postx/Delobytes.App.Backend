using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Versioned BOM line linking a product to a component. A line is in force over the half-open
/// interval [<see cref="ValidFrom"/>, <see cref="ValidTo"/>): the start date belongs to the version,
/// the end date already belongs to its successor. Superseded records are closed rather than deleted
/// so historical calculations remain reproducible.
/// </summary>
public class BomLine : ITenantScoped, IAuditableEntity, IRowVersionedEntity
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid ComponentId { get; set; }
    public decimal Quantity { get; set; }
    public DateOnly ValidFrom { get; set; }

    /// <summary>
    /// Gets or sets the first date on which the version no longer applies. Null means the version is
    /// still in force — either it is the current active one, or it was superseded before this field
    /// existed and the backfill could not date its end.
    /// </summary>
    public DateOnly? ValidTo { get; set; }

    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public uint RowVersion { get; set; }
    public Product Product { get; set; } = default!;
    public Component Component { get; set; } = default!;

    /// <summary>
    /// Ends the version at <paramref name="changeDate"/>, the date on which the composition changed.
    /// A version scheduled to start on or after that date never became effective, so it is closed
    /// with an empty interval instead of an inverted one — otherwise it would stay priced in every
    /// calculation from <paramref name="changeDate"/> onwards.
    /// </summary>
    /// <param name="changeDate">Date the version stops applying.</param>
    public void CloseAt(DateOnly changeDate)
    {
        ValidTo = ValidFrom >= changeDate ? ValidFrom : changeDate;
        IsActive = false;
    }
}
