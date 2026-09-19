namespace Delobytes.App.Backend.Catalog.Application.Queries.CostTypes.GetCostTypes;

public class GetCostTypesResponse
{
    public IReadOnlyList<CostTypeItem> Items { get; set; } = new List<CostTypeItem>();
}

public class CostTypeItem
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
