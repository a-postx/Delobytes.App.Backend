using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Domain.Enums;

namespace Delobytes.App.Backend.Finance.Domain.Calculation;

/// <summary>
/// Reduces the journal of one P&amp;L line to its totals. Pure: no clock, no persistence, no
/// knowledge of a channel beyond what the entries already carry.
/// </summary>
/// <remarks>
/// Superseded entries are skipped rather than deleted: the journal keeps every version and the
/// aggregate reads only the current one. Control totals are collected separately and never summed
/// into the result — a payout already covers the commission, the acquiring fee and the delivery
/// fee, so adding it would count them twice.
/// </remarks>
public static class PnlLineAggregator
{
    /// <summary>
    /// Aggregates the entries of one line.
    /// </summary>
    /// <param name="entries">Every entry recorded for the line, current and superseded.</param>
    /// <returns>The totals of the line.</returns>
    /// <exception cref="InvalidOperationException">
    /// An entry carries a negative amount, or a control total is not typed as a payout, or a payout
    /// is typed as a component. Each of those means the writer got the sign or the kind wrong, and
    /// summing through it would produce a wrong result silently.
    /// </exception>
    public static PnlLineTotals Calculate(IEnumerable<PnlEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        decimal revenue = 0m;
        decimal commission = 0m;
        decimal acquiring = 0m;
        decimal delivery = 0m;
        decimal refund = 0m;
        decimal controlTotal = 0m;

        int entryCount = 0;
        bool hasControlTotal = false;

        foreach (PnlEntry entry in entries)
        {
            if (entry.SupersededByEntryId != null)
            {
                continue;
            }

            if (entry.Amount < 0m)
            {
                throw new InvalidOperationException(
                    $"P&L entry {entry.Id} carries a negative amount {entry.Amount}; the sign belongs in Direction.");
            }

            if (entry.Kind == PnlEntryKind.ControlTotal)
            {
                EnsureControlTotalType(entry);

                controlTotal += Signed(entry);
                hasControlTotal = true;

                continue;
            }

            if (entry.Type == PnlEntryType.Payout)
            {
                throw new InvalidOperationException(
                    $"P&L entry {entry.Id} is typed as a payout but is not a control total; a payout must never be summed.");
            }

            entryCount++;

            decimal signed = Signed(entry);

            switch (entry.Type)
            {
                case PnlEntryType.Sale:
                    revenue += signed;
                    break;
                case PnlEntryType.Commission:
                    commission += signed;
                    break;
                case PnlEntryType.Acquiring:
                    acquiring += signed;
                    break;
                case PnlEntryType.Logistics:
                    delivery += signed;
                    break;
                case PnlEntryType.Refund:
                    refund += signed;
                    break;
                case PnlEntryType.Payout:
                    throw new InvalidOperationException(
                        $"P&L entry {entry.Id} is typed as a payout but is not a control total; a payout must never be summed.");
                default:
                    throw new InvalidOperationException(
                        $"P&L entry {entry.Id} carries unsupported type {entry.Type}.");
            }
        }

        decimal netRevenue = revenue + commission + acquiring + delivery + refund;

        return new PnlLineTotals(
            revenue,
            commission,
            acquiring,
            delivery,
            refund,
            netRevenue,
            hasControlTotal ? controlTotal : null,
            hasControlTotal ? netRevenue - controlTotal : null,
            entryCount);
    }

    private static decimal Signed(PnlEntry entry)
    {
        return entry.Direction == PnlEntryDirection.Expense ? -entry.Amount : entry.Amount;
    }

    private static void EnsureControlTotalType(PnlEntry entry)
    {
        if (entry.Type != PnlEntryType.Payout)
        {
            throw new InvalidOperationException(
                $"P&L entry {entry.Id} is a control total of type {entry.Type}; only {PnlEntryType.Payout} is a control total.");
        }
    }
}
