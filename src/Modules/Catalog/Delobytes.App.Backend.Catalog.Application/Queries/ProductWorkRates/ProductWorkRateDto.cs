namespace Delobytes.App.Backend.Catalog.Application.Queries.ProductWorkRates;

public record ProductWorkRateDto
{
    public Guid Id { get; init; }
    public Guid ProductId { get; init; }
    public int AssemblyRatePerDay { get; init; }
    public DateOnly ValidFrom { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public bool IsActive { get; init; }
}
