using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.Queries;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using Delobytes.App.Backend.Finance.Domain.Enums;

namespace Delobytes.App.Backend.Finance.Application.ProductTable;

/// <summary>
/// Collects the document gaps of one product: the pending and overdue revenue with the documents
/// still missing, and the control difference. One pass over the same lines the summary folds, so
/// the groups reconcile with the product row of the report.
/// </summary>
public static class PnlDocumentGapAggregator
{
    public static async Task<PnlProductDocumentGaps> AggregateAsync(
        IPnlReadStore store,
        ITenantSettingsProvider settingsProvider,
        Guid tenantId,
        string currency,
        PnlLineFilter filter,
        CancellationToken cancellationToken)
    {
        TaxProfileTimeline timeline = await PnlViewFactory.LoadTimelineAsync(
            tenantId,
            settingsProvider,
            cancellationToken);

        GapAccumulator pending = new();
        GapAccumulator overdue = new();
        decimal? controlDifference = null;
        int linesWithControlDifference = 0;

        await foreach (PnlLineRow row in store
            .StreamForSummaryAsync(currency, filter, cancellationToken)
            .WithCancellation(cancellationToken))
        {
            PnlLineView view = PnlViewFactory.BuildRow(row, timeline);

            // A line without a period date is outside the report, so it must not inflate a group.
            if (!view.PeriodDate.HasValue)
            {
                continue;
            }

            GapAccumulator? target = view.CompletenessState switch
            {
                PnlLineCompleteness.Provisional => pending,
                PnlLineCompleteness.Overdue => overdue,
                _ => null,
            };

            if (target != null)
            {
                target.Add(view.RevenueAmount ?? 0m, view.ExpectedBy, view.MissingDocuments);
            }

            if (row.ControlDifferenceAmount.HasValue)
            {
                controlDifference = (controlDifference ?? 0m) + row.ControlDifferenceAmount.Value;

                if (row.ControlDifferenceAmount.Value != 0m)
                {
                    linesWithControlDifference++;
                }
            }
        }

        return new PnlProductDocumentGaps
        {
            Pending = pending.Build(),
            Overdue = overdue.Build(),
            ControlDifferenceAmount = controlDifference,
            LinesWithControlDifference = linesWithControlDifference,
        };
    }

    private sealed class GapAccumulator
    {
        private readonly Dictionary<string, int> _types = new(StringComparer.Ordinal);
        private decimal _revenue;
        private DateTimeOffset? _earliest;
        private int _lines;

        public void Add(
            decimal revenue,
            DateTimeOffset? expectedBy,
            IReadOnlyList<string> missingDocuments)
        {
            _lines++;
            _revenue += revenue;

            if (expectedBy.HasValue
                && (!_earliest.HasValue || expectedBy.Value < _earliest.Value))
            {
                _earliest = expectedBy;
            }

            // A type is counted once per line: two expectations of the same document on one line are
            // still one line waiting for it.
            foreach (string type in missingDocuments.Distinct(StringComparer.Ordinal))
            {
                _types[type] = _types.TryGetValue(type, out int count) ? count + 1 : 1;
            }
        }

        public PnlDocumentGapGroup Build()
        {
            return new PnlDocumentGapGroup
            {
                Lines = _lines,
                RevenueAmount = _revenue,
                EarliestExpectedBy = _earliest,
                MissingDocuments = _types
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new PnlMissingDocumentCount
                    {
                        Type = pair.Key,
                        Lines = pair.Value,
                    })
                    .ToList(),
            };
        }
    }
}
