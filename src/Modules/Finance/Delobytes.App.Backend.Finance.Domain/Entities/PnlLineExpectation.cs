using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Domain.Enums;

namespace Delobytes.App.Backend.Finance.Domain.Entities;

/// <summary>
/// A document the channel is known to produce for a sale but has not delivered yet. The margin of a
/// line is only final once its expectations are closed: Wildberries reports the sale and the
/// delivery of the same unit as two rows of the same report, and the delivery may arrive in a later
/// report instance.
/// </summary>
/// <remarks>
/// <para>
/// This is an open item, not a flag on the line: it carries its own deadline, its own closure
/// decision and its own audit fields, so an unresolved expectation can be queued, aged and closed
/// without touching the amounts. The line only mirrors the severity of its open expectations in
/// <see cref="PnlLine.CompletenessState"/> for cheap reads.
/// </para>
/// <para>
/// The expectation is never deleted. A late document closes it, and the closure fields keep who
/// closed it and why, because a figure reported as final and later corrected must stay explainable.
/// </para>
/// </remarks>
public class PnlLineExpectation : ITenantScoped, IRowVersionedEntity
{
    /// <summary>
    /// Gets or sets the expectation unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the Sales order line the expected document belongs to.
    /// </summary>
    public Guid OrderLineId { get; set; }

    /// <summary>
    /// Gets or sets the kind of amount expected: the delivery document of the unit.
    /// </summary>
    public PnlEntryType ExpectedType { get; set; }

    /// <summary>
    /// Gets or sets the document source that produces the expected entry. The rule is driven by the
    /// source rather than by a channel identifier, because Finance stores no channel code.
    /// </summary>
    public PnlSource Source { get; set; }

    /// <summary>
    /// Gets or sets the instant the expectation becomes overdue: the reported period end plus the
    /// fixed window.
    /// </summary>
    public DateTimeOffset ExpectedBy { get; set; }

    /// <summary>
    /// Gets or sets the lifecycle state.
    /// </summary>
    public PnlExpectationState State { get; set; } = PnlExpectationState.Expected;

    /// <summary>
    /// Gets or sets the estimated impact of the missing document, or null when no comparable figure
    /// exists. Only an estimate is used to auto-close an expectation as immaterial.
    /// </summary>
    public decimal? AmountEstimate { get; set; }

    /// <summary>
    /// Gets or sets the line currency, so the materiality threshold is never applied across currencies.
    /// </summary>
    public string Currency { get; set; } = default!;

    /// <summary>
    /// Gets or sets the instant the expectation was closed, by a document or by a decision.
    /// </summary>
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>
    /// Gets or sets the administrator who closed the expectation. Null when the system closed it as
    /// immaterial, which is what distinguishes a decision from an automatic write-off.
    /// </summary>
    public Guid? ClosedByUserId { get; set; }

    /// <summary>
    /// Gets or sets the closure reason: the administrator's note, or the automatic marker.
    /// </summary>
    public string? ClosureReason { get; set; }

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
