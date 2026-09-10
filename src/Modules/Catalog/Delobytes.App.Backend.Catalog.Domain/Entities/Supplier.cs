using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Represents a supplier of packaging components.
/// </summary>
public class Supplier : ITenantScoped
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public string? ContactInfo { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public ICollection<PackagingComponent> PackagingComponents { get; set; } = new List<PackagingComponent>();
}
