namespace Delobytes.App.Backend.Catalog.Application.Queries.RawMaterialRates.GetRawMaterialRates;

public class GetRawMaterialRatesResponse
{
    public IReadOnlyList<RawMaterialRateItem> Items { get; set; } = new List<RawMaterialRateItem>();
}

public class RawMaterialRateItem
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public decimal CostPerUnit { get; set; }

    public DateOnly ValidFrom { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
