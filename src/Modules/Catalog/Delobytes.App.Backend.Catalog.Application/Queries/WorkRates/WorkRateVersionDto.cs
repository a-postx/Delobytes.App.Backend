namespace Delobytes.App.Backend.Catalog.Application.Queries.WorkRates;

/// <summary>
/// Active wage version of a work rate as exposed to the client.
/// </summary>
public class WorkRateVersionDto
{
    public Guid Id { get; set; }

    public decimal DailyWage { get; set; }

    /// <summary>Effective date in yyyy-MM-dd format.</summary>
    public string ValidFrom { get; set; } = default!;
}
