namespace Delobytes.App.Backend.Catalog.Application.Queries.ProductWorkRates;

public record ProductWorkRateDto
{
    public Guid Id { get; init; }
    public Guid ProductId { get; init; }

    /// <summary>Name of the product this version belongs to, so list clients do not need a lookup.</summary>
    public string ProductName { get; init; } = default!;

    /// <summary>SKU of the product this version belongs to; shown next to the name in the list.</summary>
    public string ProductSku { get; init; } = default!;

    public Guid WorkRateId { get; init; }
    public int AssemblyRatePerDay { get; init; }
    public DateOnly ValidFrom { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public bool IsActive { get; init; }
}
