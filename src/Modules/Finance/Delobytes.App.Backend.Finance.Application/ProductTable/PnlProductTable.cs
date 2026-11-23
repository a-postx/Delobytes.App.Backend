using Delobytes.App.Backend.Finance.Application.Queries;

namespace Delobytes.App.Backend.Finance.Application.ProductTable;

/// <summary>
/// Product table rules: the row a summary group becomes, its segment, search, counts, summary,
/// ordering and paging. Pure, so every rule is pinned by a test instead of living in a handler.
/// </summary>
public static class PnlProductTable
{
    public static PnlProductRow Map(PnlSummaryRowView row)
    {
        decimal? deductions = null;

        if (row.NetRevenueAmount.HasValue && row.RevenueAmount.HasValue)
        {
            deductions = row.NetRevenueAmount.Value - row.RevenueAmount.Value;
        }

        decimal? soldCost = row.Margin.HasValue
            ? row.ProductionCostWithMarginAmount
            : null;

        decimal? marginCoverage = null;
        decimal? finalRevenue = null;

        if (row.RevenueAmount.HasValue && row.RevenueAmount.Value > 0m)
        {
            marginCoverage = row.RevenueWithMarginAmount / row.RevenueAmount.Value * 100m;
            finalRevenue = (row.RevenueAmount.Value - row.ProvisionalRevenueAmount)
                / row.RevenueAmount.Value * 100m;
        }

        int nonBuyout = row.QuantityOrdered - row.QuantitySold;

        decimal? buyout = row.QuantityOrdered == 0
            ? null
            : (decimal)row.QuantitySold / row.QuantityOrdered * 100m;

        decimal? frozen = null;

        // The frozen part is the invested cost minus the sold cost. The two describe the same lines
        // only when every line with a margin also carries an investment, so a mixed group reports
        // the figure as unknown rather than as a difference between different populations.
        if (row.LinesWithMargin > 0 && row.LinesWithInvestedCost == row.LinesWithMargin)
        {
            frozen = row.InvestedCostAmount - row.ProductionCostWithMarginAmount;
        }

        return new PnlProductRow
        {
            ProductId = row.ProductId,
            Sku = row.Sku,
            Lines = row.Lines,
            RevenueAmount = row.RevenueAmount,
            DeductionsAmount = deductions,
            NetRevenueAmount = row.NetRevenueAmount,
            CommissionAmount = row.CommissionAmount,
            DeliveryFeeAmount = row.DeliveryFeeAmount,
            RefundAmount = row.RefundAmount,
            AcquiringAmount = row.AcquiringAmount,
            CompensationAmount = row.CompensationAmount,
            SoldCostAmount = soldCost,
            NetRevenueWithoutCostAmount = row.NetRevenueWithoutCostAmount,
            Margin = row.Margin,
            ContributionMarginPercent = row.ContributionMarginPercent,
            ReturnOnSoldCostPercent = row.ReturnOnSoldCostPercent,
            MarginCoveragePercent = marginCoverage,
            CostCoverage = ResolveCostCoverage(row),
            QuantityOrdered = row.QuantityOrdered,
            QuantitySold = row.QuantitySold,
            NonBuyoutQuantity = nonBuyout,
            BuyoutPercent = buyout,
            InvestedCostAmount = row.InvestedCostAmount,
            FrozenCostAmount = frozen,
            RoiPercent = row.RoiPercent,
            IsRoiComplete = row.IsRoiComplete,
            EstimatedTax = row.EstimatedTax,
            TaxAdjustedMargin = row.TaxAdjustedMargin,
            TaxAdjustedRoiPercent = row.TaxAdjustedRoiPercent,
            IsTaxAdjustedMarginComplete = row.IsTaxAdjustedMarginComplete,
            IsTaxAdjustedRoiComplete = row.IsTaxAdjustedRoiComplete,
            ProvisionalRevenueAmount = row.ProvisionalRevenueAmount,
            OverdueRevenueAmount = row.OverdueRevenueAmount,
            FinalRevenuePercent = finalRevenue,
            ControlDifferenceAmount = row.ControlDifferenceAmount,
            CompletenessState = row.CompletenessState,
        };
    }

    /// <summary>
    /// Classifies how much of the net revenue carries a known cost. A group without positive net
    /// revenue lacking a cost is complete: there is nothing the margin fails to cover.
    /// </summary>
    public static PnlProductCostCoverage ResolveCostCoverage(PnlSummaryRowView row)
    {
        if (row.NetRevenueWithoutCostAmount <= 0m)
        {
            return PnlProductCostCoverage.Complete;
        }

        return row.Margin.HasValue
            ? PnlProductCostCoverage.Partial
            : PnlProductCostCoverage.Missing;
    }

    public static bool IsInSegment(PnlProductRow row, PnlProductSegment segment)
    {
        return segment switch
        {
            PnlProductSegment.Loss => row.Margin.HasValue && row.Margin.Value < 0m,
            PnlProductSegment.WithoutCost => row.CostCoverage != PnlProductCostCoverage.Complete,
            _ => true,
        };
    }

    public static string? NormalizeSearch(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        return search.Trim();
    }

    public static List<PnlProductRow> ApplySearch(
        IReadOnlyList<PnlProductRow> rows,
        string? search)
    {
        if (search == null)
        {
            return rows.ToList();
        }

        // A row without a label cannot be proven to match, so it never enters a search result.
        return rows
            .Where(row => row.Sku != null
                && row.Sku.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static PnlProductSegmentCounts CountSegments(IReadOnlyList<PnlProductRow> rows)
    {
        return new PnlProductSegmentCounts
        {
            All = rows.Count,
            Loss = rows.Count(row => IsInSegment(row, PnlProductSegment.Loss)),
            WithoutCost = rows.Count(row => IsInSegment(row, PnlProductSegment.WithoutCost)),
        };
    }

    public static PnlProductSummary Summarize(IReadOnlyList<PnlProductRow> rows)
    {
        List<decimal> completePercents = new();
        int lossProducts = 0;
        decimal lossMargin = 0m;
        int withoutCostProducts = 0;
        decimal withoutCostNetRevenue = 0m;

        foreach (PnlProductRow row in rows)
        {
            if (IsInSegment(row, PnlProductSegment.Loss))
            {
                lossProducts++;
                lossMargin += row.Margin ?? 0m;
            }

            if (IsInSegment(row, PnlProductSegment.WithoutCost))
            {
                withoutCostProducts++;
                withoutCostNetRevenue += row.NetRevenueWithoutCostAmount;
            }

            if (row.CostCoverage == PnlProductCostCoverage.Complete
                && row.ContributionMarginPercent.HasValue)
            {
                completePercents.Add(row.ContributionMarginPercent.Value);
            }
        }

        return new PnlProductSummary
        {
            Products = rows.Count,
            LossProducts = lossProducts,
            LossMarginAmount = lossMargin,
            WithoutCostProducts = withoutCostProducts,
            WithoutCostNetRevenueAmount = withoutCostNetRevenue,
            MedianContributionMarginPercent = Median(completePercents),
        };
    }

    public static List<PnlProductRow> Sort(
        IReadOnlyList<PnlProductRow> rows,
        PnlProductSortKey sortBy,
        bool descending)
    {
        if (sortBy == PnlProductSortKey.Sku)
        {
            return SortBySku(rows, descending);
        }

        Func<PnlProductRow, decimal?> metric = MetricSelector(sortBy);

        // A row without the metric cannot be compared with the others and stays at the end in both
        // directions: "no data" is not the smallest value.
        List<PnlProductRow> withValue = rows.Where(row => metric(row).HasValue).ToList();
        List<PnlProductRow> withoutValue = rows.Where(row => !metric(row).HasValue).ToList();

        IOrderedEnumerable<PnlProductRow> ordered = descending
            ? withValue.OrderByDescending(metric)
            : withValue.OrderBy(metric);

        return ordered
            .ThenByDescending(row => row.RevenueAmount ?? 0m)
            .ThenBy(row => row.ProductId ?? Guid.Empty)
            .Concat(withoutValue
                .OrderByDescending(row => row.RevenueAmount ?? 0m)
                .ThenBy(row => row.ProductId ?? Guid.Empty))
            .ToList();
    }

    public static (List<PnlProductRow> Items, int Page) Slice(
        IReadOnlyList<PnlProductRow> rows,
        int page,
        int pageSize)
    {
        int size = Math.Max(1, pageSize);
        int lastPage = rows.Count == 0
            ? 1
            : (int)Math.Ceiling((double)rows.Count / size);
        int served = Math.Clamp(page, 1, lastPage);

        return (
            rows.Skip((served - 1) * size).Take(size).ToList(),
            served);
    }

    private static List<PnlProductRow> SortBySku(
        IReadOnlyList<PnlProductRow> rows,
        bool descending)
    {
        List<PnlProductRow> withValue = rows.Where(row => row.Sku != null).ToList();
        List<PnlProductRow> withoutValue = rows.Where(row => row.Sku == null).ToList();

        IOrderedEnumerable<PnlProductRow> ordered = descending
            ? withValue.OrderByDescending(row => row.Sku, StringComparer.OrdinalIgnoreCase)
            : withValue.OrderBy(row => row.Sku, StringComparer.OrdinalIgnoreCase);

        return ordered
            .ThenByDescending(row => row.RevenueAmount ?? 0m)
            .ThenBy(row => row.ProductId ?? Guid.Empty)
            .Concat(withoutValue
                .OrderByDescending(row => row.RevenueAmount ?? 0m)
                .ThenBy(row => row.ProductId ?? Guid.Empty))
            .ToList();
    }

    private static Func<PnlProductRow, decimal?> MetricSelector(PnlProductSortKey sortBy)
    {
        return sortBy switch
        {
            PnlProductSortKey.Revenue => row => row.RevenueAmount,
            PnlProductSortKey.Deductions => row => row.DeductionsAmount,
            PnlProductSortKey.NetRevenue => row => row.NetRevenueAmount,
            PnlProductSortKey.SoldCost => row => row.SoldCostAmount,
            PnlProductSortKey.Margin => row => row.Margin,
            PnlProductSortKey.ContributionMarginPercent => row => row.ContributionMarginPercent,
            PnlProductSortKey.ReturnOnSoldCostPercent => row => row.ReturnOnSoldCostPercent,
            PnlProductSortKey.QuantitySold => row => row.QuantitySold,
            PnlProductSortKey.BuyoutPercent => row => row.BuyoutPercent,
            _ => row => row.Margin,
        };
    }

    private static decimal? Median(List<decimal> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        values.Sort();
        int middle = values.Count / 2;

        if (values.Count % 2 == 0)
        {
            return (values[middle - 1] + values[middle]) / 2m;
        }

        return values[middle];
    }
}
