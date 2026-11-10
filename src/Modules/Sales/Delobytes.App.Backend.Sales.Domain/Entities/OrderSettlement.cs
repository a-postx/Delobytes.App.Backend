using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Sales.Domain.Enums;

namespace Delobytes.App.Backend.Sales.Domain.Entities;

/// <summary>
/// Resolved money for an order line, each amount carrying its provenance. One row per line.
/// </summary>
/// <remarks>
/// Facts and derived values are separated by design: <see cref="OrderLine"/> holds what the
/// channel reported, this entity holds what was resolved from it. Every amount is paired with a
/// <see cref="ValueSource"/> so a consumer can tell a channel-reported number from a configured one.
/// A null amount together with <see cref="ValueSource.Unknown"/> is a valid terminal state (for
/// instance Wildberries and Yandex Market supply no commission in their order APIs) and must be
/// surfaced as "not calculated" rather than substituted with a zero.
/// <para>
/// Amounts are stored as money, never as a rate: the channel bills a fixed sum per line, a rate
/// would have to be re-applied and could drift away from what was actually charged. A rate belongs
/// in the raw payload, which the Integrations module retains in its RawApiResponses table.
/// </para>
/// <para>
/// Tax is deliberately absent. It is charged on revenue rather than per sale, cannot be attributed
/// to one line before the reporting period closes, and is derived from the versioned tenant profile
/// instead.
/// </para>
/// <para>
/// Upgrade path, not implemented: if per-revision audit of financial facts is required later,
/// promote immutable <c>OrderSettlementEntry</c> rows — one per fact, per source, per financial
/// period — and recompute this entity from them. Today the immutable trail is the raw API payload
/// retained in the Integrations module, attributable through the JobRun of the import that produced
/// the amounts.
/// </para>
/// </remarks>
public class OrderSettlement : ITenantScoped, IRowVersionedEntity
{
    /// <summary>
    /// Gets or sets the settlement unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the order line this settlement belongs to.
    /// </summary>
    public Guid OrderLineId { get; set; }

    /// <summary>
    /// Gets or sets the commission charged by the channel. Null when not reported.
    /// </summary>
    public decimal? CommissionAmount { get; set; }

    /// <summary>
    /// Gets or sets where <see cref="CommissionAmount"/> came from.
    /// </summary>
    public ValueSource CommissionSource { get; set; } = ValueSource.Unknown;

    /// <summary>
    /// Gets or sets the acquiring fee. Null when not reported.
    /// </summary>
    public decimal? AcquiringAmount { get; set; }

    /// <summary>
    /// Gets or sets where <see cref="AcquiringAmount"/> came from.
    /// </summary>
    public ValueSource AcquiringSource { get; set; } = ValueSource.Unknown;

    /// <summary>
    /// Gets or sets the payout amount. Null when not reported.
    /// </summary>
    public decimal? PayoutAmount { get; set; }

    /// <summary>
    /// Gets or sets where <see cref="PayoutAmount"/> came from.
    /// </summary>
    public ValueSource PayoutSource { get; set; } = ValueSource.Unknown;

    /// <summary>
    /// Gets or sets the delivery fee attributed to this line. Null when not reported.
    /// </summary>
    public decimal? DeliveryFeeAmount { get; set; }

    /// <summary>
    /// Gets or sets where <see cref="DeliveryFeeAmount"/> came from.
    /// </summary>
    public ValueSource DeliveryFeeSource { get; set; } = ValueSource.Unknown;

    /// <summary>
    /// Gets or sets the refund amount. Null when not reported.
    /// </summary>
    public decimal? RefundAmount { get; set; }

    /// <summary>
    /// Gets or sets where <see cref="RefundAmount"/> came from.
    /// </summary>
    public ValueSource RefundSource { get; set; } = ValueSource.Unknown;

    /// <summary>
    /// Gets or sets the derived net revenue. Null whenever any input is missing — never zero.
    /// </summary>
    public decimal? NetRevenueAmount { get; set; }

    /// <summary>
    /// Gets or sets where <see cref="NetRevenueAmount"/> came from.
    /// </summary>
    public ValueSource NetRevenueSource { get; set; } = ValueSource.Unknown;

    /// <summary>
    /// Gets or sets the settlement state. Named after settlement, not margin: margin also needs
    /// cost of goods, which this entity does not have.
    /// </summary>
    public SettlementState State { get; set; } = SettlementState.NotCalculated;

    /// <summary>
    /// Gets or sets the settlement currency.
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

    /// <summary>
    /// Gets or sets the order line this settlement belongs to.
    /// </summary>
    public OrderLine OrderLine { get; set; } = default!;
}
