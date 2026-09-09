namespace Delobytes.App.Backend.Catalog.Application.Queries.WorkRates.GetWorkRate;

public class GetWorkRateResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public decimal DailyWage { get; set; }

    public int AssemblyRatePerDay { get; set; }

    public DateOnly ValidFrom { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
