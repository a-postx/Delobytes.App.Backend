using Delobytes.App.Backend.Catalog.Domain.Enums;

namespace Delobytes.App.Backend.Catalog.Application.Queries.BomLines;

public class BomLineDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid ComponentId { get; set; }
    public decimal Quantity { get; set; }
    public DateOnly ValidFrom { get; set; }

    /// <summary>
    /// Gets or sets the first date on which this version is no longer in force; null while it is
    /// still in force. Replaces the former boolean of the same spirit, which could not say when.
    /// </summary>
    public DateOnly? ValidTo { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public BomComponentDto? Component { get; set; }
}

public class BomComponentDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public Unit Unit { get; set; }
    public ComponentCategory Category { get; set; }
}
