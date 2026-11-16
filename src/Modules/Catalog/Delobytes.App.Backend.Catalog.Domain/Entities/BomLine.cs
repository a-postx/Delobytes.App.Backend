using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Versioned BOM line linking a product to a component. A line is in force over the half-open
/// interval <c>[ValidFrom, ValidTo)</c>, which makes every composition reproducible for any past
/// date, including the dates on which a component left the composition.
/// </summary>
public class BomLine : ITenantScoped, IAuditableEntity, IRowVersionedEntity
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid ComponentId { get; set; }
    public decimal Quantity { get; set; }

    /// <summary>
    /// Gets or sets the first date on which this version is in force.
    /// </summary>
    public DateOnly ValidFrom { get; set; }

    /// <summary>
    /// Gets or sets the first date on which this version is no longer in force; null while the
    /// version is still in force. A boolean "is active" flag cannot express this: it says that a
    /// component stopped being part of the composition but not from which date, so a cost
    /// calculated for a past date could not tell the two states apart and kept charging components
    /// that had already been removed.
    /// </summary>
    public DateOnly? ValidTo { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public uint RowVersion { get; set; }
    public Product Product { get; set; } = default!;
    public Component Component { get; set; } = default!;

    /// <summary>
    /// Returns a value indicating whether this version is in force on <paramref name="asOf"/>.
    /// The repository query implements the same rule in SQL; both must stay in step.
    /// </summary>
    /// <param name="asOf">Date the composition is resolved for.</param>
    /// <returns>True when the version covers the date.</returns>
    public bool IsEffectiveAt(DateOnly asOf)
    {
        return ValidFrom <= asOf && (ValidTo == null || asOf < ValidTo);
    }

    /// <summary>
    /// Dates the moment this version stops being in force. Closing on or before <see cref="ValidFrom"/>
    /// produces an empty interval rather than an inverted one: a version withdrawn on the day it was
    /// meant to start was never in force, and moving ValidTo before ValidFrom would make every
    /// interval comparison meaningless.
    /// </summary>
    /// <param name="date">First date on which the version is no longer in force.</param>
    public void CloseAt(DateOnly date)
    {
        ValidTo = date > ValidFrom ? date : ValidFrom;
    }
}
