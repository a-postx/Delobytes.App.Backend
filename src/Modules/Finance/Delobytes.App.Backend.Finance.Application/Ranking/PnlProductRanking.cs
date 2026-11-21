using Delobytes.App.Backend.Finance.Application.Queries;

namespace Delobytes.App.Backend.Finance.Application.Ranking;

/// <summary>Metric a product ranking is ordered by.</summary>
public enum PnlRankingSortKey
{
    Margin = 0,
    Roi = 1,
    ContributionMarginPercent = 2,
    Revenue = 3,
    NetRevenue = 4,
    QuantitySold = 5,
    QuantityOrdered = 6,
}

/// <summary>
/// Whether a ranking accepts rows whose cost coverage is partial. A partial margin is a real figure
/// over part of the group, but comparing it with a complete one ranks products by how much of their
/// cost is known, so a ranking defaults to complete coverage.
/// </summary>
public enum PnlRankingCoverage
{
    Any = 0,
    Complete = 1,
}

/// <summary>The ranked slice and the population it was taken from.</summary>
public sealed class PnlRankingResult
{
    /// <summary>Gets the ranked rows, <c>Rank</c> assigned from 1.</summary>
    public List<PnlSummaryRowView> Rows { get; init; } = new();

    /// <summary>Gets the number of rows that carried the ranked metric and passed the filters.</summary>
    public int RankedRows { get; init; }

    /// <summary>Gets the number of rows the request produced before ranking.</summary>
    public int TotalRows { get; init; }
}

/// <summary>
/// Ranks product summary rows by one metric and slices the top. The rules are deliberate: a row
/// without the metric is not comparable and never pads the list, partial coverage can be excluded,
/// and ties fall back to revenue and then to the product identifier so the order is stable.
/// </summary>
public static class PnlProductRanking
{
    public static PnlRankingResult Rank(
        IReadOnlyList<PnlSummaryRowView> rows,
        PnlRankingSortKey sortKey,
        bool descending,
        int take,
        PnlRankingCoverage coverage,
        decimal? minRevenue)
    {
        IEnumerable<PnlSummaryRowView> candidates = rows;

        if (minRevenue.HasValue)
        {
            decimal threshold = minRevenue.Value;
            candidates = candidates.Where(row => (row.RevenueAmount ?? 0m) >= threshold);
        }

        candidates = ApplyCoverage(candidates, sortKey, coverage);

        // A row without the ranked metric cannot be compared with the others: a top-N list must not
        // be padded with unknowns.
        List<PnlSummaryRowView> ranked = candidates
            .Where(row => Metric(row, sortKey).HasValue)
            .ToList();

        IEnumerable<PnlSummaryRowView> ordered = descending
            ? ranked
                .OrderByDescending(row => Metric(row, sortKey)!.Value)
                .ThenByDescending(row => row.RevenueAmount ?? 0m)
                .ThenBy(row => row.ProductId ?? Guid.Empty)
            : ranked
                .OrderBy(row => Metric(row, sortKey)!.Value)
                .ThenByDescending(row => row.RevenueAmount ?? 0m)
                .ThenBy(row => row.ProductId ?? Guid.Empty);

        List<PnlSummaryRowView> slice = ordered
            .Take(Math.Max(take, 0))
            .ToList();

        for (int index = 0; index < slice.Count; index++)
        {
            slice[index].Rank = index + 1;
        }

        return new PnlRankingResult
        {
            Rows = slice,
            RankedRows = ranked.Count,
            TotalRows = rows.Count,
        };
    }

    private static decimal? Metric(PnlSummaryRowView row, PnlRankingSortKey sortKey)
    {
        return sortKey switch
        {
            PnlRankingSortKey.Margin => row.Margin,
            PnlRankingSortKey.Roi => row.RoiPercent,
            PnlRankingSortKey.ContributionMarginPercent => row.ContributionMarginPercent,
            PnlRankingSortKey.Revenue => row.RevenueAmount,
            PnlRankingSortKey.NetRevenue => row.NetRevenueAmount,
            PnlRankingSortKey.QuantitySold => row.QuantitySold,
            PnlRankingSortKey.QuantityOrdered => row.QuantityOrdered,
            _ => null,
        };
    }

    private static IEnumerable<PnlSummaryRowView> ApplyCoverage(
        IEnumerable<PnlSummaryRowView> rows,
        PnlRankingSortKey sortKey,
        PnlRankingCoverage coverage)
    {
        if (coverage != PnlRankingCoverage.Complete)
        {
            return rows;
        }

        return sortKey switch
        {
            PnlRankingSortKey.Margin or PnlRankingSortKey.ContributionMarginPercent =>
                rows.Where(row => row.IsMarginComplete),
            PnlRankingSortKey.Roi => rows.Where(row => row.IsRoiComplete),
            _ => rows,
        };
    }
}
