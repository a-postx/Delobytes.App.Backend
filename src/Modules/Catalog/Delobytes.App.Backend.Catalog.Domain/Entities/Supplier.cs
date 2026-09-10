using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Represents a supplier of packaging components.
/// </summary>
public class Supplier : ITenantScoped
{
    public Guid Id { get; set; }

    /// <summary>ИНН — 10 digits for legal entities, 12 for individuals.</summary>
    public string Inn { get; set; } = default!;

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public ICollection<PackagingComponent> PackagingComponents { get; set; } = new List<PackagingComponent>();
}
