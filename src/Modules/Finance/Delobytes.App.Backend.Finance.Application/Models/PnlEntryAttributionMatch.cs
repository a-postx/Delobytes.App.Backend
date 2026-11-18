using Delobytes.App.Backend.Finance.Domain.Entities;

namespace Delobytes.App.Backend.Finance.Application.Models;

/// <summary>
/// An entry attributed to an order line.
/// </summary>
/// <param name="OrderLineId">The order line the entry belongs to.</param>
/// <param name="Entry">The journal entry.</param>
public sealed record PnlEntryAttributionMatch(Guid OrderLineId, PnlEntry Entry);
