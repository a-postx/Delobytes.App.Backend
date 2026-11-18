namespace Delobytes.App.Backend.Sales.Application.Models;

/// <summary>
/// The change state of one order line, derived from EF change tracking while the entry states are
/// still available.
/// </summary>
/// <param name="OrderLineId">Order line identifier.</param>
/// <param name="IsNew">True when the row was added by this import.</param>
/// <param name="CostRelevantChange">
/// True when the line is new or when its external product, catalogue link, quantity or order date
/// changed — the facts a production cost is resolved from.
/// </param>
public sealed record OrderLineChange(Guid OrderLineId, bool IsNew, bool CostRelevantChange);
