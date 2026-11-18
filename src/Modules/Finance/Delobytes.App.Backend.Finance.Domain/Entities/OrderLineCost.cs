using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Domain.Enums;

namespace Delobytes.App.Backend.Finance.Domain.Entities;

/// <summary>
/// The production cost resolved by Catalog for one Sales order line, as of the tenant-local order
/// date. A separate row from <see cref="PnlLine"/> because it arrives independently of the journal:
/// a cost can be known before the money and can be re-resolved after it, and the P&amp;L line must be
/// rebuildable from both facts.
/// </summary>
public class OrderLineCost : ITenantScoped, IRowVersionedEntity
{
    /// <summary>
    /// Gets or sets the Sales order line identifier. The primary key as well: one cost per line.
    /// </summary>
    public Guid OrderLineId { get; set; }

    /// <summary>
    /// Gets or sets whether Catalog could resolve a product for the line.
    /// </summary>
    public OrderLineCostStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the catalogue product the cost was calculated for.
    /// </summary>
    public Guid? ProductId { get; set; }

    /// <summary>
    /// Gets or sets the date the cost inputs were resolved against.
    /// </summary>
    public DateOnly AsOfDate { get; set; }

    /// <summary>
    /// Gets or sets the line quantity the unit costs were multiplied by.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Gets or sets the material cost of one unit.
    /// </summary>
    public decimal? UnitMaterialCost { get; set; }

    /// <summary>
    /// Gets or sets the logistics cost of one unit.
    /// </summary>
    public decimal? UnitLogisticsCost { get; set; }

    /// <summary>
    /// Gets or sets the packaging cost of one unit.
    /// </summary>
    public decimal? UnitPackagingCost { get; set; }

    /// <summary>
    /// Gets or sets the labour cost of one unit.
    /// </summary>
    public decimal? UnitLaborCost { get; set; }

    /// <summary>
    /// Gets or sets the total cost of one unit.
    /// </summary>
    public decimal? UnitTotalCost { get; set; }

    /// <summary>
    /// Gets or sets the total cost of the whole line: the unit total multiplied by the quantity.
    /// </summary>
    public decimal? TotalCost { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether every cost input was available on the as-of date.
    /// </summary>
    public bool IsComplete { get; set; }

    /// <summary>
    /// Gets or sets the warnings that made the calculation incomplete, as Catalog reported them.
    /// </summary>
    public List<string> WarningTypes { get; set; } = new List<string>();

    /// <summary>
    /// Gets or sets the instant Catalog resolved the result. The latest resolve wins.
    /// </summary>
    public DateTimeOffset ResolvedAt { get; set; }

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the last modification timestamp.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the row version for optimistic concurrency (PostgreSQL xmin).
    /// </summary>
    public uint RowVersion { get; set; }
}
