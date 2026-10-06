using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Immutable daily wage version for a work rate.
/// Changing the wage creates a new record with a new ValidFrom;
/// existing records are never modified so historical cost calculations remain intact.
/// </summary>
public class WorkRateVersion : ITenantScoped, IAuditableEntity, IRowVersionedEntity
{
    public Guid Id { get; set; }

    public Guid WorkRateId { get; set; }

    /// <summary>Average gross daily wage for one assembly worker, in currency units.</summary>
    public decimal DailyWage { get; set; }

    /// <summary>Date from which this wage version becomes effective.</summary>
    public DateOnly ValidFrom { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set only on deactivation/restore; never changes ValidFrom.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public uint RowVersion { get; set; }

    public WorkRate WorkRate { get; set; } = default!;
}
