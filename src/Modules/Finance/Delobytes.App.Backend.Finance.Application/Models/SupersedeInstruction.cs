namespace Delobytes.App.Backend.Finance.Application.Models;

/// <summary>
/// One supersede instruction: <paramref name="SupersededEntryId"/> is replaced by
/// <paramref name="SupersedingEntryId"/> at <paramref name="SupersededAt"/>.
/// </summary>
public readonly record struct SupersedeInstruction(
    Guid SupersededEntryId,
    Guid SupersedingEntryId,
    DateTimeOffset SupersededAt);
