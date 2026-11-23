using Delobytes.App.Backend.Finance.Application.Queries;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using Delobytes.App.Backend.Finance.Domain.Enums;

namespace Delobytes.App.Backend.Finance.Application.Aggregation;

/// <summary>
/// Folds line views into the totals of one summary group. Every figure is accumulated where it
/// belongs, so no intermediate list per column is allocated and the coverage parts stay disjoint
/// by construction.
/// </summary>
public sealed class PnlSummaryGroup
{
    private decimal? _revenue;
    private decimal? _commission;
    private decimal? _acquiring;
    private decimal? _deliveryFee;
    private decimal? _refund;
    private decimal? _compensation;
    private decimal? _netRevenue;
    private decimal? _productionCost;
    private decimal? _estimatedTax;
    private decimal? _margin;
    private decimal? _taxAdjustedMargin;
    private decimal? _controlDifference;
    private decimal _revenueWithMargin;
    private decimal _productionCostWithMargin;
    private decimal _revenueWithoutCost;
    private decimal _netRevenueWithMargin;
    private decimal _netRevenueWithoutCost;
    private decimal _taxAdjustedRevenue;
    private decimal _taxAdjustedNetRevenue;
    private decimal? _marginWithInvestedCost;
    private decimal _investedCost;
    private decimal _taxAdjustedInvestedCost;
    private int _linesWithInvestedCost;
    private decimal _provisionalRevenue;
    private decimal _overdueRevenue;
    private decimal _closedWithExceptionRevenue;
    private int _lines;
    private int _linesWithMargin;
    private int _overdueLines;
    private int _linesWithTaxAdjustedMargin;
    private int _linesWithTaxAdjustedInvestedCost;
    private int _quantityOrdered;
    private int _quantitySold;
    private PnlLineCompleteness _worst = PnlLineCompleteness.Complete;

    /// <summary>
    /// Initializes a new instance of the <see cref="PnlSummaryGroup"/> class.
    /// </summary>
    /// <param name="groupId">Identifier of the grouping dimension, or null for a dimensionless or unlinked group.</param>
    /// <param name="periodStart">Start of the period bucket the group belongs to.</param>
    public PnlSummaryGroup(Guid? groupId, DateOnly periodStart)
    {
        GroupId = groupId;
        PeriodStart = periodStart;
    }

    /// <summary>Gets the identifier of the grouping dimension, or null when the group carries none.</summary>
    public Guid? GroupId { get; }

    /// <summary>Gets the start of the period bucket the group was folded into.</summary>
    public DateOnly PeriodStart { get; }

    /// <summary>
    /// Gets the earliest period date folded into the group. A total-period group is keyed by a
    /// sentinel, so this is what the reported period start falls back to.
    /// </summary>
    public DateOnly? EarliestPeriodDate { get; private set; }

    /// <summary>
    /// Gets the latest period date folded into the group. A total-period group reports it as the
    /// period end when the filter has no upper bound.
    /// </summary>
    public DateOnly? LatestPeriodDate { get; private set; }

    // A period without lines: the money it would have carried is zero, not unknown, so the
    // fields accumulate from zero. The margin stays null, as no line has a cost to derive it.
    public static PnlSummaryGroup EmptyPeriod(Guid? groupId, DateOnly periodStart)
    {
        return new PnlSummaryGroup(groupId, periodStart)
        {
            _revenue = 0m,
            _commission = 0m,
            _acquiring = 0m,
            _deliveryFee = 0m,
            _refund = 0m,
            _compensation = 0m,
            _netRevenue = 0m,
            _productionCost = 0m,
            _estimatedTax = 0m,
            _controlDifference = 0m,
            _netRevenueWithMargin = 0m,
            _netRevenueWithoutCost = 0m,
            _investedCost = 0m,
        };
    }

    public void Add(PnlLineView view, decimal? controlDifference)
    {
        _lines++;

        _revenue = Accumulate(_revenue, view.RevenueAmount);

        // The deduction components are summed over every line of the group, like the revenue they
        // split it into: a component no line reported stays unknown instead of becoming zero, and
        // the identity revenue + components = net revenue holds for the group because a missing
        // component contributes nothing to either side.
        _commission = Accumulate(_commission, view.CommissionAmount);
        _acquiring = Accumulate(_acquiring, view.AcquiringAmount);
        _deliveryFee = Accumulate(_deliveryFee, view.DeliveryFeeAmount);
        _refund = Accumulate(_refund, view.RefundAmount);
        _compensation = Accumulate(_compensation, view.CompensationAmount);

        _netRevenue = Accumulate(_netRevenue, view.NetRevenueAmount);
        _productionCost = Accumulate(_productionCost, view.ProductionCostAmount);
        _estimatedTax = Accumulate(_estimatedTax, view.EstimatedTax);
        _taxAdjustedMargin = Accumulate(_taxAdjustedMargin, view.TaxAdjustedMargin);
        _controlDifference = Accumulate(_controlDifference, controlDifference);

        // Both quantities are summed over every line of the group: the ordered units include a
        // non-buyout, the sold ones do not, and the two must describe the same population of
        // lines so a per-unit figure can be derived from either without a second scan.
        _quantityOrdered += view.QuantityOrdered;
        _quantitySold += view.QuantitySold;

        if (!EarliestPeriodDate.HasValue || view.PeriodDate < EarliestPeriodDate.Value)
        {
            EarliestPeriodDate = view.PeriodDate;
        }

        if (!LatestPeriodDate.HasValue || view.PeriodDate > LatestPeriodDate.Value)
        {
            LatestPeriodDate = view.PeriodDate;
        }

        // The margin exists only for a line with a known and complete cost, so it is summed over
        // its own population. Every figure derived from it is summed over the same population,
        // and the rest of the revenue is reported separately, so no percentage pairs a numerator
        // and a denominator that describe different lines.
        if (view.Margin.HasValue)
        {
            _linesWithMargin++;
            _margin = Accumulate(_margin, view.Margin);
            _revenueWithMargin += view.RevenueAmount ?? 0m;
            _productionCostWithMargin += view.ProductionCostAmount ?? 0m;
            _netRevenueWithMargin += view.NetRevenueAmount ?? 0m;
        }
        else
        {
            _revenueWithoutCost += view.RevenueAmount ?? 0m;
            _netRevenueWithoutCost += view.NetRevenueAmount ?? 0m;
        }

        // The ROI population is an intersection: a margin to show a return on and a known cost of
        // the ordered units to divide it by. Its numerator is kept apart from the full margin, so
        // the percentage never pairs a numerator and a denominator that describe different lines.
        if (view.Margin.HasValue && view.InvestedCostAmount.HasValue)
        {
            _marginWithInvestedCost = Accumulate(_marginWithInvestedCost, view.Margin);
            _investedCost += view.InvestedCostAmount.Value;
            _linesWithInvestedCost++;
        }

        // The tax layer has its own population, narrower than the margin one: a line needs both a
        // known margin and a resolved profile. Every tax-adjusted figure is accumulated here, so
        // its percentage pairs the same lines as its own denominator.
        if (view.TaxAdjustedMargin.HasValue)
        {
            _linesWithTaxAdjustedMargin++;
            _taxAdjustedRevenue += view.RevenueAmount ?? 0m;
            _taxAdjustedNetRevenue += view.NetRevenueAmount ?? 0m;

            if (view.InvestedCostAmount.HasValue)
            {
                _taxAdjustedInvestedCost += view.InvestedCostAmount.Value;
                _linesWithTaxAdjustedInvestedCost++;
            }
        }

        // The provisional part is the same population the line view flags, so the badge and the
        // filter scopes cannot describe different lines.
        if (PnlLineCompletenessSemantics.IsProvisional(view.CompletenessState))
        {
            _provisionalRevenue += view.RevenueAmount ?? 0m;
        }

        if (view.CompletenessState == PnlLineCompleteness.Overdue)
        {
            _overdueLines++;
            _overdueRevenue += view.RevenueAmount ?? 0m;
        }

        if (view.CompletenessState == PnlLineCompleteness.ClosedWithException)
        {
            _closedWithExceptionRevenue += view.RevenueAmount ?? 0m;
        }

        if (Severity(view.CompletenessState) > Severity(_worst))
        {
            _worst = view.CompletenessState;
        }
    }

    public PnlSummaryRowView Build(
        PnlSummaryGroupBy groupBy,
        DateOnly periodStart,
        DateOnly periodEnd,
        bool isPartial)
    {
        bool byChannel = groupBy == PnlSummaryGroupBy.Channel;
        bool dimensionless = groupBy == PnlSummaryGroupBy.None;

        // A percentage is reported only when its own population is the whole group: otherwise the
        // figure would be correct for some lines and silently read as if it described all of them.
        bool taxMarginComplete = _lines > 0 && _linesWithTaxAdjustedMargin == _lines;
        bool taxRoiComplete = _lines > 0 && _linesWithTaxAdjustedInvestedCost == _lines;

        return new PnlSummaryRowView
        {
            // The dimension decides which identifier the row carries: a product row must not
            // expose a channel as if it were a product, and vice versa. A dimensionless row
            // carries neither, and an empty identifier there is not an unresolved product.
            ProductId = !byChannel && !dimensionless ? GroupId : null,
            ChannelId = byChannel ? GroupId : null,
            IsUnlinkedProduct = !byChannel && !dimensionless && GroupId == null,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            IsPartial = isPartial,
            Lines = _lines,
            RevenueAmount = _revenue,
            CommissionAmount = _commission,
            AcquiringAmount = _acquiring,
            DeliveryFeeAmount = _deliveryFee,
            RefundAmount = _refund,
            CompensationAmount = _compensation,
            NetRevenueAmount = _netRevenue,
            ProductionCostAmount = _productionCost,
            EstimatedTax = _estimatedTax,
            Margin = _margin,
            TaxAdjustedMargin = _taxAdjustedMargin,
            ContributionMarginPercent =
                PnlTaxCalculator.ContributionMarginPercent(_margin, _netRevenueWithMargin),
            TaxAdjustedContributionMarginPercent = taxMarginComplete
                ? PnlTaxCalculator.ContributionMarginPercent(_taxAdjustedMargin, _taxAdjustedNetRevenue)
                : null,
            ReturnOnSoldCostPercent =
                PnlTaxCalculator.ReturnOnSoldCostPercent(_margin, _productionCostWithMargin),
            RoiPercent = PnlTaxCalculator.RoiPercent(_marginWithInvestedCost, _investedCost),
            TaxAdjustedRoiPercent = taxRoiComplete
                ? PnlTaxCalculator.RoiPercent(_taxAdjustedMargin, _taxAdjustedInvestedCost)
                : null,
            QuantityOrdered = _quantityOrdered,
            QuantitySold = _quantitySold,
            LinesWithMargin = _linesWithMargin,
            LinesWithTaxAdjustedMargin = _linesWithTaxAdjustedMargin,
            RevenueWithMarginAmount = _revenueWithMargin,
            ProductionCostWithMarginAmount = _productionCostWithMargin,
            RevenueWithoutCostAmount = _revenueWithoutCost,
            NetRevenueWithMarginAmount = _netRevenueWithMargin,
            NetRevenueWithoutCostAmount = _netRevenueWithoutCost,
            RevenueWithTaxAdjustedMarginAmount = _taxAdjustedRevenue,
            NetRevenueWithTaxAdjustedMarginAmount = _taxAdjustedNetRevenue,
            InvestedCostAmount = _investedCost,
            LinesWithInvestedCost = _linesWithInvestedCost,
            InvestedCostWithTaxAdjustedMarginAmount = _taxAdjustedInvestedCost,
            LinesWithTaxAdjustedInvestedCost = _linesWithTaxAdjustedInvestedCost,
            // A group with no lines at all has no margin to be complete about, so an empty
            // period must not report itself as fully covered.
            IsMarginComplete = _lines > 0 && _linesWithMargin == _lines,
            IsTaxAdjustedMarginComplete = taxMarginComplete,
            // A line without any cost leaves the investment unknown, so a group is ROI-complete
            // only when its cost fact is known for every line.
            IsRoiComplete = _lines > 0 && _linesWithInvestedCost == _lines,
            IsTaxAdjustedRoiComplete = taxRoiComplete,
            ControlDifferenceAmount = _controlDifference,
            ProvisionalRevenueAmount = _provisionalRevenue,
            OverdueLines = _overdueLines,
            OverdueRevenueAmount = _overdueRevenue,
            ClosedWithExceptionRevenueAmount = _closedWithExceptionRevenue,
            CompletenessState = _worst,
        };
    }

    // The enum order is not an order of severity: a recorded exception is final, while an overdue
    // document is still missing, so the numeric value cannot be relied on.
    private static int Severity(PnlLineCompleteness state)
    {
        return state switch
        {
            PnlLineCompleteness.Overdue => 3,
            PnlLineCompleteness.Provisional => 2,
            PnlLineCompleteness.ClosedWithException => 1,
            _ => 0,
        };
    }

    // Sum over the values that are present, keeping the accumulator null while nothing is known:
    // an empty group of figures is unknown, not zero.
    private static decimal? Accumulate(decimal? accumulator, decimal? value)
    {
        if (!value.HasValue)
        {
            return accumulator;
        }

        return accumulator.HasValue ? accumulator.Value + value.Value : value.Value;
    }
}
