using Delobytes.App.Backend.Catalog.Application.Queries.WorkRates;

namespace Delobytes.App.Backend.Catalog.Application.Queries.WorkRates.GetWorkRate;

public class GetWorkRateResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    /// <summary>
    /// Kept for backward compatibility: sourced from <see cref="ActiveVersion"/>, zero when there is none.
    /// </summary>
    public decimal DailyWage { get; set; }

    /// <summary>
    /// Kept for backward compatibility: sourced from <see cref="ActiveVersion"/>, default when there is none.
    /// </summary>
    public DateOnly ValidFrom { get; set; }

    /// <summary>Currently active wage version, or null when the work rate has no active wage.</summary>
    public WorkRateVersionDto? ActiveVersion { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
