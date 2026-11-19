using Delobytes.App.Backend.Finance.Domain.Enums;

namespace Delobytes.App.Backend.Finance.Application.Models;

/// <summary>
/// The idempotency key of a journal entry: two entries sharing it are the same channel fact.
/// </summary>
/// <remarks>
/// <see cref="ChannelId"/> is part of the key because the channel-side identifiers are unique per
/// seller account, not per tenant: two shops of one marketplace in the same tenant number their
/// reports and, for Ozon, their accruals independently. Omitting it would drop the second shop's
/// rows as duplicates of the first. The channel identifier is stable across credentials rotation,
/// unlike the connection identifier, so it is the account identity the journal keys on.
/// </remarks>
public readonly record struct JournalIdentityKey(
    Guid ChannelId,
    PnlSource Source,
    string ReportInstanceKey,
    string ExternalEntryId,
    string ExternalProductId,
    PnlEntryKind Kind,
    PnlEntryType Type);
