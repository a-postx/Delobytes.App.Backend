using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Domain.Enums;
using Delobytes.App.Backend.Integrations.Contracts.Models;

namespace Delobytes.App.Backend.Finance.Application.Ingestion;

/// <summary>
/// Maps the ingestion contract to the Finance journal. The mapping is explicit and exhaustive: an
/// unmapped enum value fails instead of silently becoming a valid but wrong entry.
/// </summary>
public static class FinanceEntryMapper
{
    /// <summary>
    /// Maps one contract entry to a journal entry.
    /// </summary>
    /// <param name="snapshot">Contract entry.</param>
    /// <param name="channelId">Channel the batch was fetched from.</param>
    /// <param name="importedAt">Import instant stamped on the journal entry.</param>
    /// <returns>The journal entry.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The snapshot carries an unmapped enum value.</exception>
    public static PnlEntry Map(
        ChannelFinanceEntrySnapshot snapshot,
        Guid channelId,
        DateTimeOffset importedAt)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new PnlEntry
        {
            Source = MapSource(snapshot.Source),
            ReportInstanceKey = snapshot.ReportInstanceKey,
            ExternalEntryId = snapshot.ExternalEntryId,
            ChannelId = channelId,
            ExternalOrderId = snapshot.ExternalOrderId,
            ExternalLineId = snapshot.ExternalLineId,
            ExternalProductId = snapshot.ExternalProductId,
            Kind = MapKind(snapshot.Kind),
            Type = MapType(snapshot.Type),
            Amount = snapshot.Amount,
            Direction = MapDirection(snapshot.Direction),
            Currency = snapshot.Currency,
            DocumentDate = snapshot.DocumentDate,
            PeriodFrom = snapshot.PeriodFrom,
            PeriodTo = snapshot.PeriodTo,
            ImportedAt = importedAt,
            CreatedAt = importedAt,
        };
    }

    /// <summary>
    /// Maps the contract source to the journal source.
    /// </summary>
    /// <param name="source">Contract value.</param>
    /// <returns>Journal value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not mapped.</exception>
    public static PnlSource MapSource(FinanceEntrySource source)
    {
        return source switch
        {
            FinanceEntrySource.WbReport => PnlSource.WbReport,
            FinanceEntrySource.OzonAccrual => PnlSource.OzonAccrual,
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unmapped finance entry source."),
        };
    }

    /// <summary>
    /// Maps the contract kind to the journal kind.
    /// </summary>
    /// <param name="kind">Contract value.</param>
    /// <returns>Journal value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not mapped.</exception>
    public static PnlEntryKind MapKind(FinanceEntryKind kind)
    {
        return kind switch
        {
            FinanceEntryKind.Component => PnlEntryKind.Component,
            FinanceEntryKind.ControlTotal => PnlEntryKind.ControlTotal,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unmapped finance entry kind."),
        };
    }

    /// <summary>
    /// Maps the contract type to the journal type.
    /// </summary>
    /// <param name="type">Contract value.</param>
    /// <returns>Journal value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not mapped.</exception>
    public static PnlEntryType MapType(FinanceEntryType type)
    {
        return type switch
        {
            FinanceEntryType.Sale => PnlEntryType.Sale,
            FinanceEntryType.Commission => PnlEntryType.Commission,
            FinanceEntryType.Acquiring => PnlEntryType.Acquiring,
            FinanceEntryType.Logistics => PnlEntryType.Logistics,
            FinanceEntryType.Refund => PnlEntryType.Refund,
            FinanceEntryType.Payout => PnlEntryType.Payout,
            FinanceEntryType.Compensation => PnlEntryType.Compensation,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unmapped finance entry type."),
        };
    }

    /// <summary>
    /// Maps the contract direction to the journal direction.
    /// </summary>
    /// <param name="direction">Contract value.</param>
    /// <returns>Journal value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not mapped.</exception>
    public static PnlEntryDirection MapDirection(FinanceEntryDirection direction)
    {
        return direction switch
        {
            FinanceEntryDirection.Income => PnlEntryDirection.Income,
            FinanceEntryDirection.Expense => PnlEntryDirection.Expense,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unmapped finance entry direction."),
        };
    }
}
