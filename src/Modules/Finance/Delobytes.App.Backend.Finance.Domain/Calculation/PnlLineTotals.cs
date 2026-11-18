namespace Delobytes.App.Backend.Finance.Domain.Calculation;

/// <summary>
/// The result of aggregating the journal of one P&amp;L line. Component amounts are signed: income
/// is positive, an expense is negative. The control total is reported as the channel sent it and
/// never participates in <see cref="NetRevenueAmount"/>.
/// </summary>
/// <param name="RevenueAmount">Signed sum of the sale entries.</param>
/// <param name="CommissionAmount">Signed sum of the commission entries.</param>
/// <param name="AcquiringAmount">Signed sum of the acquiring entries.</param>
/// <param name="DeliveryFeeAmount">Signed sum of the delivery entries.</param>
/// <param name="RefundAmount">Signed sum of the refund entries.</param>
/// <param name="NetRevenueAmount">Sum of every component — the money the sale left before cost of goods.</param>
/// <param name="ControlTotalAmount">Signed sum of the control totals, or null when the channel reported none.</param>
/// <param name="ControlDifferenceAmount">
/// <see cref="NetRevenueAmount"/> minus <see cref="ControlTotalAmount"/>, or null when there is no
/// control total. Non-zero means the components do not explain what the channel paid.
/// </param>
/// <param name="EntryCount">Number of component entries behind the totals; zero means nothing has arrived yet.</param>
public sealed record PnlLineTotals(
    decimal RevenueAmount,
    decimal CommissionAmount,
    decimal AcquiringAmount,
    decimal DeliveryFeeAmount,
    decimal RefundAmount,
    decimal NetRevenueAmount,
    decimal? ControlTotalAmount,
    decimal? ControlDifferenceAmount,
    int EntryCount);
