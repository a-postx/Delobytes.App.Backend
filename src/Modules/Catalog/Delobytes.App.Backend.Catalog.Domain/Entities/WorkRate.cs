using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Represents a work rate entry used to calculate assembly/packaging labour cost.
/// Each entry is versioned via ValidFrom; the record active at a given date is used for cost calculation.
/// </summary>
public class WorkRate : ITenantScoped
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    /// <summary>Average gross daily wage for one assembly worker, in currency units.</summary>
    public decimal DailyWage { get; set; }

    /// <summary>Date from which this rate becomes effective.</summary>
    public DateOnly ValidFrom { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
