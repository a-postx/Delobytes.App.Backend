using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// An ordered template that composes an identifier out of constant and auto-number segments,
/// for example <c>P-000001</c>. At most one scheme per tenant and entity kind is the default,
/// and only the default scheme issues identifiers.
/// </summary>
public class NumberingScheme : ITenantScoped, IAuditableEntity, IRowVersionedEntity
{
    public Guid Id { get; set; }

    /// <summary>Stable lookup key, for example <c>PRODUCT</c>.</summary>
    public string Code { get; set; } = default!;

    public string Name { get; set; } = default!;

    public NumberingEntityKind EntityKind { get; set; }

    /// <summary>Selects this scheme for its entity kind; inactive defaults keep occupying the slot.</summary>
    public bool IsDefault { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public uint RowVersion { get; set; }

    public ICollection<NumberingSegment> Segments { get; set; } = new List<NumberingSegment>();
}
