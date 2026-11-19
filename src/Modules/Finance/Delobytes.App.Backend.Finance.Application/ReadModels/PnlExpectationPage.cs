using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Domain.Enums;

namespace Delobytes.App.Backend.Finance.Application.ReadModels;

/// <summary>
/// Filter of the expectation queue: the operational list of documents that were expected and have
/// not arrived.
/// </summary>
public sealed class PnlExpectationFilter
{
    /// <summary>Gets or sets the optional state filter.</summary>
    public PnlExpectationState? State { get; set; }

    /// <summary>Gets or sets the optional lower bound of <see cref="PnlLineExpectation.ExpectedBy"/>.</summary>
    public DateTimeOffset? DueFrom { get; set; }

    /// <summary>Gets or sets the optional upper bound of <see cref="PnlLineExpectation.ExpectedBy"/>.</summary>
    public DateTimeOffset? DueTo { get; set; }

    /// <summary>Gets or sets the optional order line filter.</summary>
    public Guid? OrderLineId { get; set; }
}

/// <summary>
/// One keyset page of the expectation queue.
/// </summary>
public sealed class PnlExpectationPage
{
    /// <summary>Gets or sets the rows of the page.</summary>
    public IReadOnlyList<PnlLineExpectation> Rows { get; set; } = Array.Empty<PnlLineExpectation>();

    /// <summary>Gets or sets the cursor of the next page, or null when the page is the last one.</summary>
    public string? NextToken { get; set; }
}

/// <summary>
/// Result of a state change requested on one expectation.
/// </summary>
public enum PnlExpectationCloseStatus
{
    /// <summary>The expectation was moved to the requested state.</summary>
    Applied = 0,

    /// <summary>No expectation with that identifier exists for the tenant.</summary>
    NotFound = 1,

    /// <summary>The expectation is not in a state the requested transition starts from.</summary>
    WrongState = 2,
}

/// <summary>
/// Outcome of a state change: the line whose completeness must be recomputed, and whether the
/// change was applied.
/// </summary>
/// <param name="Status">Whether the change was applied.</param>
/// <param name="OrderLineId">Order line of the expectation, when it exists.</param>
public sealed record PnlExpectationCloseOutcome(
    PnlExpectationCloseStatus Status,
    Guid? OrderLineId);
