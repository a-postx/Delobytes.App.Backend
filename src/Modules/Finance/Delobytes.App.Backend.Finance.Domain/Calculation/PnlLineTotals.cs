namespace Delobytes.App.Backend.Finance.Domain.Calculation;

/// <summary>
/// The result of aggregating the journal of one P&amp;L line. Component amounts are signed: income
/// is positive, an expense is negative.
/// </summary>
/// <remarks>
/// Every component is nullable and stays <c>null</c> when the journal holds no current entry of that
/// type. "Not reported" is not the same as zero: a channel that never charges acquiring is different
/// from one that reported a zero fee, and a zero would hide the difference. The control total is
/// reported as the channel sent it and never participates in <see cref="NetRevenueAmount"/>.
/// </remarks>
/// <param name="RevenueAmount">Signed sum of the sale entries, or null when none arrived.</param>
/// <param name="CommissionAmount">Signed sum of the commission entries, or null when none arrived.</param>
/// <param name="AcquiringAmount">Signed sum of the acquiring entries, or null when none arrived.</param>
/// <param name="DeliveryFeeAmount">Signed sum of the delivery entries, or null when none arrived.</param>
/// <param name="RefundAmount">Signed sum of the refund entries, or null when none arrived.</param>
/// <param name="CompensationAmount">
/// Signed sum of the marketplace-funded discount compensation entries, or null when none arrived.
/// </param>
/// <param name="NetRevenueAmount">
/// Sum of every reported component - the money the sale left before cost of goods. Null when no
/// component arrived at all.
/// </param>
/// <param name="ControlTotalAmount">Signed sum of the control totals, or null when the channel reported none.</param>
/// <param name="ControlDifferenceAmount">
/// <see cref="NetRevenueAmount"/> minus <see cref="ControlTotalAmount"/>, or null when there is no
/// control total. Non-zero means the components do not explain what the channel paid.
/// </param>
/// <param name="EntryCount">Number of component entries behind the totals; zero means nothing has arrived yet.</param>
/// <param name="HasSale">True when at least one current sale entry exists.</param>
/// <param name="SaleDate">The earliest <c>DocumentDate</c> of the current sale entries, or null.</param>
public sealed record PnlLineTotals(
    decimal? RevenueAmount,
    decimal? CommissionAmount,
    decimal? AcquiringAmount,
    decimal? DeliveryFeeAmount,
    decimal? RefundAmount,
    decimal? CompensationAmount,
    decimal? NetRevenueAmount,
    decimal? ControlTotalAmount,
    decimal? ControlDifferenceAmount,
    int EntryCount,
    bool HasSale,
    DateOnly? SaleDate);
