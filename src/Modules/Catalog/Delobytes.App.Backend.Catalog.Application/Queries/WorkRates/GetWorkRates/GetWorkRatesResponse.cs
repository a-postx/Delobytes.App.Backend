namespace Delobytes.App.Backend.Catalog.Application.Queries.WorkRates.GetWorkRates;

public class GetWorkRatesResponse
{
    public IReadOnlyList<WorkRateItem> Items { get; set; } = new List<WorkRateItem>();
}

public class WorkRateItem
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public decimal DailyWage { get; set; }

    public int AssemblyRatePerDay { get; set; }

    public DateOnly ValidFrom { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
