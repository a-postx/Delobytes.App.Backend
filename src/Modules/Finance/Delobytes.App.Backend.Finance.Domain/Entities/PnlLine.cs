using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Domain.Enums;

namespace Delobytes.App.Backend.Finance.Domain.Entities;

/// <summary>
/// The result of one sale: the totals aggregated from the <see cref="PnlEntry"/> journal of a
/// single Sales order line, plus the production cost resolved for that line. Derived, never entered
/// by hand.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OrderLineId"/> is a soft reference to the Sales module. No foreign key can exist:
/// the row lives in another module, another schema and another DbContext, and the modules may only
/// know each other through <c>*.Contracts</c>. The correspondence is enforced by the unique index
/// on <c>(TenantId, OrderLineId)</c> and by the writer, not by the database.
/// </para>
/// <para>
/// Every amount here is a sum over the journal, so this row must stay rebuildable from it: the only
/// writer is the projection that recomputes a line when its entries change, and a full rebuild from
/// the journal must always produce the same values. Component amounts are signed — income positive,
/// an expense negative — because they are summed from entries that carry their direction explicitly.
/// </para>
/// <para>
/// <see cref="ControlTotalAmount"/> is the net cash movement the channel reports for the unit, not
/// "the money the channel remitted": a Wildberries logistics row reports money leaving the seller,
/// so a payout entry may be an expense. It is deliberately excluded from
/// <see cref="NetRevenueAmount"/> because it already covers the commission, the acquiring fee and
/// the delivery fee. <see cref="ControlDifferenceAmount"/> is what the components fail to explain; a
/// non-zero value is a defect in the mapping or a legitimately skipped channel operation, not a
/// rounding artefact.
/// </para>
/// <para>
/// The row is rebuilt from the journal and the cost fact, so it must stay reproducible: the only
/// writer is the projection that recomputes a line when either changes, and a full rebuild must
/// always produce the same values.
/// </para>
/// <para>
/// A line with no entries is simply not closed yet — that is what
/// <see cref="PnlLineState.NotCalculated"/> means. There is no separate "waiting" flag: absence of
/// entries is the only thing to wait for, and whether money will ever arrive is a property of the
/// Sales line, not of this row.
/// </para>
/// <para>
/// Tax is deliberately absent. It is charged on revenue rather than per sale, cannot be attributed
/// to one line before the reporting period closes, and is derived from the versioned tenant profile
/// instead.
/// </para>
/// </remarks>
public class PnlLine : ITenantScoped, IRowVersionedEntity
{
    /// <summary>
    /// Gets or sets the P&amp;L line unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the Sales order line the totals belong to. A soft reference
    /// across module boundaries — see the type remarks.
    /// </summary>
    public Guid OrderLineId { get; set; }

    /// <summary>
    /// Gets or sets the signed sum of the sale entries.
    /// </summary>
    public decimal? RevenueAmount { get; set; }

    /// <summary>
    /// Gets or sets where <see cref="RevenueAmount"/> came from.
    /// </summary>
    public ValueSource RevenueSource { get; set; } = ValueSource.Unknown;

    /// <summary>
    /// Gets or sets the signed sum of the commission entries.
    /// </summary>
    public decimal? CommissionAmount { get; set; }

    /// <summary>
    /// Gets or sets where <see cref="CommissionAmount"/> came from.
    /// </summary>
    public ValueSource CommissionSource { get; set; } = ValueSource.Unknown;

    /// <summary>
    /// Gets or sets the signed sum of the acquiring entries.
    /// </summary>
    public decimal? AcquiringAmount { get; set; }

    /// <summary>
    /// Gets or sets where <see cref="AcquiringAmount"/> came from.
    /// </summary>
    public ValueSource AcquiringSource { get; set; } = ValueSource.Unknown;

    /// <summary>
    /// Gets or sets the signed sum of the delivery entries.
    /// </summary>
    public decimal? DeliveryFeeAmount { get; set; }

    /// <summary>
    /// Gets or sets where <see cref="DeliveryFeeAmount"/> came from.
    /// </summary>
    public ValueSource DeliveryFeeSource { get; set; } = ValueSource.Unknown;

    /// <summary>
    /// Gets or sets the signed sum of the refund entries.
    /// </summary>
    public decimal? RefundAmount { get; set; }

    /// <summary>
    /// Gets or sets where <see cref="RefundAmount"/> came from.
    /// </summary>
    public ValueSource RefundSource { get; set; } = ValueSource.Unknown;

    /// <summary>
    /// Gets or sets the signed sum of the marketplace-funded discount compensation entries.
    /// </summary>
    public decimal? CompensationAmount { get; set; }

    /// <summary>
    /// Gets or sets where <see cref="CompensationAmount"/> came from.
    /// </summary>
    public ValueSource CompensationSource { get; set; } = ValueSource.Unknown;

    /// <summary>
    /// Gets or sets the signed sum of every component — the money the sale left before cost of
    /// goods.
    /// </summary>
    public decimal? NetRevenueAmount { get; set; }

    /// <summary>
    /// Gets or sets where <see cref="NetRevenueAmount"/> came from.
    /// </summary>
    public ValueSource NetRevenueSource { get; set; } = ValueSource.Unknown;

    /// <summary>
    /// Gets or sets the channel-reported total for the money it remitted. Null when the channel
    /// reported none. Excluded from <see cref="NetRevenueAmount"/>.
    /// </summary>
    public decimal? ControlTotalAmount { get; set; }

    /// <summary>
    /// Gets or sets <see cref="NetRevenueAmount"/> minus <see cref="ControlTotalAmount"/>. Null
    /// when there is no control total; non-zero means the components do not explain the payout.
    /// </summary>
    public decimal? ControlDifferenceAmount { get; set; }

    /// <summary>
    /// Gets or sets the production cost resolved for the line, or null while no cost fact exists.
    /// Applied only when the line has a sale; a logistics-only row keeps its negative result without
    /// cost of goods.
    /// </summary>
    public decimal? ProductionCostAmount { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the production cost covers every input. Null when
    /// there is no cost fact.
    /// </summary>
    public bool? ProductionCostIsComplete { get; set; }

    /// <summary>
    /// Gets or sets the date of the earliest sale entry. The tax profile is selected against it.
    /// </summary>
    public DateOnly? SaleDate { get; set; }

    /// <summary>
    /// Gets or sets the number of units of the line the journal reverses. A returned unit left the
    /// seller and came back, so it carries no cost of goods and its result is the money the return
    /// left behind. The sold quantity is what is left of the ordered units.
    /// </summary>
    public int ReturnedQuantity { get; set; }

    /// <summary>
    /// Gets or sets the number of component entries behind the totals. Zero means the journal holds
    /// nothing for this line yet.
    /// </summary>
    public int EntryCount { get; set; }

    /// <summary>
    /// Gets or sets the time the totals were last recomputed from the journal.
    /// </summary>
    public DateTimeOffset? ComputedAt { get; set; }

    /// <summary>
    /// Gets or sets the line state. Derived from the journal: entries present means calculated,
    /// none means nothing has arrived yet.
    /// </summary>
    public PnlLineState State { get; set; } = PnlLineState.NotCalculated;

    /// <summary>
    /// Gets or sets whether every document the sale is expected to produce has arrived. A line can
    /// be <see cref="PnlLineState.Calculated"/> and still be
    /// <see cref="PnlLineCompleteness.Provisional"/>, because the delivery document of the unit is
    /// reported separately and may arrive later.
    /// </summary>
    public PnlLineCompleteness CompletenessState { get; set; } = PnlLineCompleteness.Complete;

    /// <summary>
    /// Gets or sets the entry types the line is still waiting for, by name. Empty when nothing is
    /// outstanding.
    /// </summary>
    public List<string> MissingDocumentTypes { get; set; } = new List<string>();

    /// <summary>
    /// Gets or sets the earliest deadline among the open expectations, or null when none is open.
    /// </summary>
    public DateTimeOffset? ExpectedBy { get; set; }

    /// <summary>
    /// Gets or sets the line currency.
    /// </summary>
    public string Currency { get; set; } = default!;

    /// <summary>
    /// Gets or sets the start of the financial period the amounts belong to, where known.
    /// </summary>
    public DateOnly? FinancialPeriodFrom { get; set; }

    /// <summary>
    /// Gets or sets the end of the financial period the amounts belong to, where known.
    /// </summary>
    public DateOnly? FinancialPeriodTo { get; set; }

    /// <summary>
    /// Gets or sets the time the amounts were last resolved. Moves on every re-resolve, because a
    /// channel may replace a provisional figure with the finalised one.
    /// </summary>
    public DateTimeOffset? ResolvedAt { get; set; }

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
