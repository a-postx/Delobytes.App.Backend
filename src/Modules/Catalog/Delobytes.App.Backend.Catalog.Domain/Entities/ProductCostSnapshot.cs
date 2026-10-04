using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Immutable historical fact describing a product cost before an input change.
/// The record is append-only and is never recalculated retroactively.
/// </summary>
public class ProductCostSnapshot : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public DateOnly AsOfDate { get; set; }
    public decimal MaterialCost { get; set; }
    public decimal LogisticsCost { get; set; }
    public decimal PackagingCost { get; set; }
    public decimal LaborCost { get; set; }
    public decimal TotalCost { get; set; }
    public bool IsComplete { get; set; }
    public string LinesSnapshotJson { get; set; } = default!;
    public string TriggerReason { get; set; } = default!;
    public DateTimeOffset CalculatedAt { get; set; }
    public Product Product { get; set; } = default!;
}
