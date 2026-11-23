using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.Queries;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using FluentValidation;
using FluentValidation.Results;

namespace Delobytes.App.Backend.Finance.Application.Aggregation;

/// <summary>The folded groups of a summary request and the money its currency filter kept out.</summary>
public sealed class PnlSummaryAggregationResult
{
    public List<PnlSummaryGroup> Groups { get; init; } = new();

    public List<PnlCurrencyExclusion> ExcludedCurrencies { get; init; } = new();
}

/// <summary>
/// The same fold read along several dimensions at once, plus one currency exclusion list: the
/// exclusions do not depend on the dimension, and a report that needs totals, channels and products
/// must not read the lines three times to get them.
/// </summary>
public sealed class PnlSummaryAggregationSet
{
    public IReadOnlyDictionary<PnlSummaryGroupBy, List<PnlSummaryGroup>> Groups { get; init; } =
        new Dictionary<PnlSummaryGroupBy, List<PnlSummaryGroup>>();

    public List<PnlCurrencyExclusion> ExcludedCurrencies { get; init; } = new();
}

/// <summary>
/// Folds the P&amp;L lines of a request into summary groups and enforces the limits of the
/// resulting grid. It knows nothing about a response: rows are built by
/// <see cref="PnlSummaryProjector"/>, so a ranking can reuse the same fold without duplicating it.
/// </summary>
public static class PnlSummaryAggregator
{
    // The period axis is bounded by PnlPeriodBuckets.MaxPeriods, but the other axis is the number
    // of groups, which no date range bounds. The ceiling is set far above a real report: it turns
    // an out-of-memory response into a rejected request.
    private const int MaxSummaryRows = 200_000;

    public static async Task<PnlSummaryAggregationResult> AggregateAsync(
        IPnlReadStore store,
        ITenantSettingsProvider settingsProvider,
        Guid tenantId,
        string currency,
        PnlLineFilter filter,
        PnlSummaryGroupBy groupBy,
        PnlPeriodGranularity granularity,
        int? maxGroups,
        CancellationToken cancellationToken)
    {
        PnlSummaryAggregationSet set = await AggregateAsync(
            store,
            settingsProvider,
            tenantId,
            currency,
            filter,
            new[] { groupBy },
            granularity,
            maxGroups,
            cancellationToken);

        return new PnlSummaryAggregationResult
        {
            Groups = set.Groups[groupBy],
            ExcludedCurrencies = set.ExcludedCurrencies,
        };
    }

    /// <summary>
    /// Folds the lines once and reports them along every requested dimension. A caller that shows
    /// totals, channels and products side by side reads the period once instead of once per axis;
    /// the peak memory stays bounded by the sum of the group counts.
    /// </summary>
    /// <param name="store">Read port of the P&amp;L lines.</param>
    /// <param name="settingsProvider">Provider of the tax profile timeline.</param>
    /// <param name="tenantId">Tenant the report belongs to.</param>
    /// <param name="currency">Reporting currency the lines are read in.</param>
    /// <param name="filter">Filters of the report.</param>
    /// <param name="groupBys">Dimensions to fold at once; a repeated axis is folded once.</param>
    /// <param name="granularity">Length of a period bucket.</param>
    /// <param name="maxGroups">Response budget of every axis, or null when the caller aggregates rows itself.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<PnlSummaryAggregationSet> AggregateAsync(
        IPnlReadStore store,
        ITenantSettingsProvider settingsProvider,
        Guid tenantId,
        string currency,
        PnlLineFilter filter,
        IReadOnlyList<PnlSummaryGroupBy> groupBys,
        PnlPeriodGranularity granularity,
        int? maxGroups,
        CancellationToken cancellationToken)
    {
        TaxProfileTimeline timeline = await PnlViewFactory.LoadTimelineAsync(
            tenantId,
            settingsProvider,
            cancellationToken);

        // Grouped by the period date rather than by the sale date: a non-buyout never sold, but its
        // delivery charge is money of the period it was booked in and must be reported. Rows are
        // folded as they arrive, so the request never holds the whole history: peak memory follows
        // the number of groups (products or channels, times periods), not the number of lines.
        Dictionary<PnlSummaryGroupBy, Dictionary<(Guid? GroupId, DateOnly PeriodStart), PnlSummaryGroup>> folds =
            groupBys
                .Distinct()
                .ToDictionary(
                    axis => axis,
                    axis => new Dictionary<(Guid? GroupId, DateOnly PeriodStart), PnlSummaryGroup>());

        await foreach (PnlLineRow row in store
            .StreamForSummaryAsync(currency, filter, cancellationToken)
            .WithCancellation(cancellationToken))
        {
            PnlLineView view = PnlViewFactory.BuildRow(row, timeline);

            // A line without any date belongs to no period and is not reported.
            if (!view.PeriodDate.HasValue)
            {
                continue;
            }

            DateOnly periodStart = PnlPeriodBuckets.StartOf(view.PeriodDate.Value, granularity);

            foreach ((PnlSummaryGroupBy axis, Dictionary<(Guid? GroupId, DateOnly PeriodStart), PnlSummaryGroup> groups) in folds)
            {
                (Guid? GroupId, DateOnly PeriodStart) key = (
                    axis switch
                    {
                        PnlSummaryGroupBy.Channel => view.ChannelId,
                        PnlSummaryGroupBy.None => null,
                        _ => view.ProductId,
                    },
                    periodStart);

                if (!groups.TryGetValue(key, out PnlSummaryGroup? group))
                {
                    if (groups.Count >= MaxSummaryRows)
                    {
                        throw new ValidationException(new[]
                        {
                            new ValidationFailure(
                                nameof(GetPnlSummaryQuery.Filter),
                                $"Сводка получилась слишком большой: сузьте диапазон, гранулярность или фильтры. Строк больше {MaxSummaryRows}."),
                        });
                    }

                    // A response budget, unlike the OOM ceiling above: stop as soon as the payload is
                    // known to be too large, instead of folding the whole history first.
                    if (maxGroups.HasValue && groups.Count >= maxGroups.Value)
                    {
                        throw new ValidationException(new[]
                        {
                            new ValidationFailure(
                                nameof(GetPnlSummaryQuery.Filter),
                                $"Сводка превышает лимит {maxGroups.Value} строк ответа. Сузьте диапазон, выберите гранулярность week или month либо ограничьте товары через productIds (до {PnlQueryFilter.MaxProductIds})."),
                        });
                    }

                    group = new PnlSummaryGroup(key.GroupId, key.PeriodStart);
                    groups.Add(key, group);
                }

                group.Add(view, row.ControlDifferenceAmount);
            }
        }

        foreach ((PnlSummaryGroupBy axis, Dictionary<(Guid? GroupId, DateOnly PeriodStart), PnlSummaryGroup> groups) in folds)
        {
            // The validator measures a closed range before the query runs. An open range takes one of
            // its edges from the data, so the same ceiling is enforced here, after the fold.
            EnsurePeriodLimit(groups, filter, granularity);

            // A dimensionless trend is one series the client plots as-is, so its periods must be
            // contiguous. A grouped summary stays sparse: a product without lines in a period is not a
            // zero, and a synthetic row cannot name the groups that never appeared.
            FillEmptyPeriods(groups, axis, granularity, filter);
        }

        // The fold reads one currency only. The money of the other currencies is reported next to
        // the totals rather than dropped: a report that silently loses revenue cannot be reconciled.
        IReadOnlyList<PnlCurrencyExclusion> excludedCurrencies = await store.GetCurrencyExclusionsAsync(
            currency,
            filter,
            cancellationToken);

        return new PnlSummaryAggregationSet
        {
            Groups = folds.ToDictionary(axis => axis.Key, axis => axis.Value.Values.ToList()),
            ExcludedCurrencies = excludedCurrencies.ToList(),
        };
    }

    // Adds a zero row for every period of the dimensionless series that has no lines, so a chart
    // drawn from the payload has no silent holes. Total is skipped because its single group is
    // keyed by a sentinel that must not surface as a real date.
    private static void FillEmptyPeriods(
        Dictionary<(Guid? GroupId, DateOnly PeriodStart), PnlSummaryGroup> groups,
        PnlSummaryGroupBy groupBy,
        PnlPeriodGranularity granularity,
        PnlLineFilter filter)
    {
        if (groupBy != PnlSummaryGroupBy.None || granularity == PnlPeriodGranularity.Total)
        {
            return;
        }

        if (!TryResolveGrid(filter, granularity, groups, out DateOnly firstBucket, out DateOnly lastBucket))
        {
            return;
        }

        (Guid? GroupId, DateOnly PeriodStart) key = (null, firstBucket);

        while (key.PeriodStart <= lastBucket)
        {
            groups.TryAdd(key, PnlSummaryGroup.EmptyPeriod(key.GroupId, key.PeriodStart));

            // The cap is checked against the resolved grid, not here: an edge may come from the last
            // line, and the next period after the last one may overflow DateOnly.
            if (key.PeriodStart == lastBucket)
            {
                break;
            }

            key = (null, PnlPeriodBuckets.Next(key.PeriodStart, granularity));
        }
    }

    // A closed range is measured by the validator before the query runs. With an open bound one edge
    // is only known after the fold, so the same ceiling is applied here; the aggregator is also the
    // backstop for a caller that sends the request without the pipeline.
    private static void EnsurePeriodLimit(
        Dictionary<(Guid? GroupId, DateOnly PeriodStart), PnlSummaryGroup> groups,
        PnlLineFilter filter,
        PnlPeriodGranularity granularity)
    {
        if (granularity == PnlPeriodGranularity.Total)
        {
            return;
        }

        if (!TryResolveGrid(filter, granularity, groups, out DateOnly firstBucket, out DateOnly lastBucket))
        {
            return;
        }

        if (PnlPeriodBuckets.Count(firstBucket, lastBucket, granularity) <= PnlPeriodBuckets.MaxPeriods)
        {
            return;
        }

        throw new ValidationException(new[]
        {
            new ValidationFailure(
                nameof(GetPnlSummaryQuery.Filter),
                $"Диапазон не может превышать {PnlPeriodBuckets.MaxPeriods} периодов выбранной гранулярности."),
        });
    }

    // The grid is the requested range when a bound is given and the observed span otherwise: history
    // has no known edges to extend to.
    private static bool TryResolveGrid(
        PnlLineFilter filter,
        PnlPeriodGranularity granularity,
        Dictionary<(Guid? GroupId, DateOnly PeriodStart), PnlSummaryGroup> groups,
        out DateOnly firstBucket,
        out DateOnly lastBucket)
    {
        if (filter.From.HasValue)
        {
            firstBucket = PnlPeriodBuckets.StartOf(filter.From.Value, granularity);
        }
        else if (groups.Count > 0)
        {
            firstBucket = groups.Keys.Min(key => key.PeriodStart);
        }
        else
        {
            firstBucket = default;
            lastBucket = default;
            return false;
        }

        if (filter.To.HasValue)
        {
            lastBucket = PnlPeriodBuckets.StartOf(filter.To.Value, granularity);
        }
        else if (groups.Count > 0)
        {
            lastBucket = groups.Keys.Max(key => key.PeriodStart);
        }
        else
        {
            lastBucket = default;
            return false;
        }

        return true;
    }
}
