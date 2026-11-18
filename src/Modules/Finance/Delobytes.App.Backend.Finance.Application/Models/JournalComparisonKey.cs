using Delobytes.App.Backend.Finance.Domain.Enums;

namespace Delobytes.App.Backend.Finance.Application.Models;

/// <summary>
/// The identity of a channel fact independent of the report instance that carried it. Used to tell
/// whether a new report instance repeats a fact already recorded under an older instance.
/// </summary>
public readonly record struct JournalComparisonKey(
    PnlSource Source,
    string ExternalEntryId,
    string ExternalProductId,
    PnlEntryKind Kind,
    PnlEntryType Type);
