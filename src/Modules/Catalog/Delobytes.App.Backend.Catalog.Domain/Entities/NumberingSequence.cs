using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Holds the counter rules of an auto-number segment: where numbering starts, how far it steps and
/// where it ends. The counter never depends on the calendar, so there is no reset period.
/// </summary>
public class NumberingSequence : ITenantScoped, IAuditableEntity, IRowVersionedEntity
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    /// <summary>Lowest number this sequence may issue.</summary>
    public int StartNumber { get; set; } = 1;

    /// <summary>Distance between two consecutive numbers; must be greater than zero.</summary>
    public int Step { get; set; } = 1;

    /// <summary>Highest number this sequence may issue; null means unbounded.</summary>
    public int? EndNumber { get; set; }

    /// <summary>The number the next allocation issues.</summary>
    public int NextNumber { get; set; }

    /// <summary>When true, exceeding <see cref="EndNumber"/> fails instead of counting on.</summary>
    public bool WarnOnClose { get; set; }

    /// <summary>Stored only; gaps are permitted and never rejected.</summary>
    public bool WarnIfNotContinuous { get; set; }

    /// <summary>Reserved for a manual numbering mode that does not exist yet.</summary>
    public bool ManualNumbering { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public uint RowVersion { get; set; }
}
