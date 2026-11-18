using Delobytes.App.Backend.Finance.Domain.Enums;

namespace Delobytes.App.Backend.Finance.Application.Models;

/// <summary>
/// The idempotency key of a journal entry: two entries sharing it are the same channel fact.
/// </summary>
public readonly record struct JournalIdentityKey(
    PnlSource Source,
    string ReportInstanceKey,
    string ExternalEntryId,
    string ExternalProductId,
    PnlEntryKind Kind,
    PnlEntryType Type);
