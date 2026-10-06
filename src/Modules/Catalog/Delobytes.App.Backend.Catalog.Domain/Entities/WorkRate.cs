using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Represents a work rate (role) used to calculate assembly labour cost.
/// Carries descriptive data only; the daily wage lives in <see cref="WorkRateVersion"/>
/// versions so that historical cost calculations remain intact.
/// </summary>
public class WorkRate : ITenantScoped, IAuditableEntity, IRowVersionedEntity
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    /// <summary>Whether the logical work rate is archived. Does not depend on wage versions.</summary>
    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public uint RowVersion { get; set; }

    public ICollection<WorkRateVersion> Versions { get; set; } = new List<WorkRateVersion>();

    public ICollection<ProductWorkRate> ProductWorkRates { get; set; } = new List<ProductWorkRate>();
}
