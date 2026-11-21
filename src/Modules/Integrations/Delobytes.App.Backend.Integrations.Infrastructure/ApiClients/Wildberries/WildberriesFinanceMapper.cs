using System.Globalization;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Wildberries.Dtos;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Wildberries;

/// <summary>
/// Maps the Wildberries weekly detailed sales report to finance entries. A pure mapper: it sees
/// only the rows and returns the entries plus the counters describing what it skipped.
/// </summary>
internal static class WildberriesFinanceMapper
{
    private const string SaleOperation = "Продажа";
    private const string ReturnOperation = "Возврат";
    private const string LogisticsOperation = "Логистика";
    private const decimal ControlTolerance = 0.01m;

    private static readonly string[] RequestedFields =
    {
        "reportId",
        "createDate",
        "dateFrom",
        "dateTo",
        "currency",
        "rrdId",
        "nmId",
        "srid",
        "sellerOperName",
        "saleDt",
        "rrDate",
        "retailAmount",
        "retailPriceWithDisc",
        "commissionPercent",
        "acquiringFee",
        "vw",
        "vwNds",
        "ppvzReward",
        "forPay",
        "deliveryService",
    };

    /// <summary>
    /// Gets the field list the client requests. Public within the assembly so a test can pin it.
    /// </summary>
    public static IReadOnlyList<string> Fields => RequestedFields;

    /// <summary>
    /// Maps one page of report rows.
    /// </summary>
    /// <param name="rows">Report rows.</param>
    /// <returns>The entries and the mapping counters.</returns>
    public static WildberriesFinanceMappingResult Map(IReadOnlyList<WildberriesFinanceReportRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        List<ChannelFinanceEntrySnapshot> entries = new();
        FinanceMappingDiagnostics diagnostics = new();

        foreach (WildberriesFinanceReportRow row in rows)
        {
            bool isSale = string.Equals(row.SellerOperName, SaleOperation, StringComparison.Ordinal);
            bool isReturn = string.Equals(row.SellerOperName, ReturnOperation, StringComparison.Ordinal);

            if (!isSale
                && !isReturn
                && !string.Equals(row.SellerOperName, LogisticsOperation, StringComparison.Ordinal))
            {
                diagnostics.SkippedOperations++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.Srid))
            {
                diagnostics.RowsWithoutSrid++;
                continue;
            }

            if (!TryResolveDocumentDate(row, diagnostics, out DateOnly documentDate))
            {
                continue;
            }

            DateOnly periodFrom = TryParseDate(row.DateFrom, out DateOnly parsedFrom) ? parsedFrom : documentDate;
            DateOnly periodTo = TryParseDate(row.DateTo, out DateOnly parsedTo) ? parsedTo : documentDate;

            string reportInstanceKey = string.Create(
                CultureInfo.InvariantCulture,
                $"{row.ReportId}:{row.CreateDate}");

            string currency = string.IsNullOrWhiteSpace(row.Currency) ? "RUB" : row.Currency;
            string externalProductId = row.NmId.ToString(CultureInfo.InvariantCulture);
            string externalEntryId = row.RrdId.ToString(CultureInfo.InvariantCulture);

            if (isSale || isReturn)
            {
                decimal retail = row.RetailAmount ?? 0m;

                // A return repeats the sale it reverses: the buyer's payment and the marketplace
                // discount go back to the buyer, the commission and the acquiring fee go back to the
                // seller. The report states the amounts of a return as positive, so the operation
                // name, not the sign, is what says which way the money moves.
                bool reversal = isReturn || retail < 0m;

                if (reversal)
                {
                    diagnostics.Reversals++;
                }

                if (isReturn)
                {
                    diagnostics.Returns++;
                }

                // The report separates the buyer's payment from the seller's price, and the gap is
                // the marketplace-funded discount (СПП), which is revenue for the seller. Where both
                // figures and the commission rate are present, the row is booked as its parts and
                // checked against forPay; the composite below stays for rows that do not report
                // enough to split, because booking the sale at the buyer's payment alone understates
                // revenue and hides the commission inside an income adjustment.
                SaleSplit split = default;

                if (retail != 0m && TryMapSellerPriceSplit(row, retail, reversal, out split))
                {
                    Emit(
                        entries,
                        row,
                        reportInstanceKey,
                        externalEntryId,
                        externalProductId,
                        currency,
                        documentDate,
                        periodFrom,
                        periodTo,
                        FinanceEntryKind.Component,
                        FinanceEntryType.Sale,
                        split.BuyerPaid,
                        reversal ? FinanceEntryDirection.Expense : FinanceEntryDirection.Income);

                    if (split.Compensation != 0m)
                    {
                        Emit(
                            entries,
                            row,
                            reportInstanceKey,
                            externalEntryId,
                            externalProductId,
                            currency,
                            documentDate,
                            periodFrom,
                            periodTo,
                            FinanceEntryKind.Component,
                            FinanceEntryType.Compensation,
                            split.Compensation,
                            reversal ? FinanceEntryDirection.Expense : FinanceEntryDirection.Income);
                    }

                    if (split.Commission != 0m)
                    {
                        Emit(
                            entries,
                            row,
                            reportInstanceKey,
                            externalEntryId,
                            externalProductId,
                            currency,
                            documentDate,
                            periodFrom,
                            periodTo,
                            FinanceEntryKind.Component,
                            FinanceEntryType.Commission,
                            split.Commission,
                            reversal ? FinanceEntryDirection.Income : FinanceEntryDirection.Expense);
                    }

                    if (split.Acquiring != 0m)
                    {
                        Emit(
                            entries,
                            row,
                            reportInstanceKey,
                            externalEntryId,
                            externalProductId,
                            currency,
                            documentDate,
                            periodFrom,
                            periodTo,
                            FinanceEntryKind.Component,
                            FinanceEntryType.Acquiring,
                            split.Acquiring,
                            reversal ? FinanceEntryDirection.Income : FinanceEntryDirection.Expense);
                    }
                }
                else
                {
                    // A negative sale row reverses the sale but keeps the fee amounts as charges, so
                    // its components follow their own sign. A return row reports the same fee as
                    // money coming back, so only there is the direction inverted.
                    if (retail != 0m)
                    {
                        diagnostics.SplitFallbacks++;

                        Emit(
                            entries,
                            row,
                            reportInstanceKey,
                            externalEntryId,
                            externalProductId,
                            currency,
                            documentDate,
                            periodFrom,
                            periodTo,
                            FinanceEntryKind.Component,
                            FinanceEntryType.Sale,
                            retail,
                            (retail >= 0m) != isReturn
                                ? FinanceEntryDirection.Income
                                : FinanceEntryDirection.Expense);
                    }

                    decimal acquiring = row.AcquiringFee ?? 0m;

                    if (acquiring != 0m)
                    {
                        Emit(
                            entries,
                            row,
                            reportInstanceKey,
                            externalEntryId,
                            externalProductId,
                            currency,
                            documentDate,
                            periodFrom,
                            periodTo,
                            FinanceEntryKind.Component,
                            FinanceEntryType.Acquiring,
                            acquiring,
                            (acquiring >= 0m) != isReturn
                                ? FinanceEntryDirection.Expense
                                : FinanceEntryDirection.Income);
                    }

                    // Fallback: the commission is the signed composite of vw, its VAT and the reward.
                    // It is a net figure — the marketplace-funded discount is already folded inside —
                    // so it is the only self-consistent reading of a row that does not report the
                    // seller's price or the commission rate.
                    decimal commission = (row.Vw ?? 0m) + (row.VwNds ?? 0m) + (row.PpvzReward ?? 0m);

                    if (commission != 0m)
                    {
                        Emit(
                            entries,
                            row,
                            reportInstanceKey,
                            externalEntryId,
                            externalProductId,
                            currency,
                            documentDate,
                            periodFrom,
                            periodTo,
                            FinanceEntryKind.Component,
                            FinanceEntryType.Commission,
                            commission,
                            (commission >= 0m) != isReturn
                                ? FinanceEntryDirection.Expense
                                : FinanceEntryDirection.Income);
                    }
                }

                decimal forPay = row.ForPay ?? 0m;

                if (forPay != 0m)
                {
                    Emit(
                        entries,
                        row,
                        reportInstanceKey,
                        externalEntryId,
                        externalProductId,
                        currency,
                        documentDate,
                        periodFrom,
                        periodTo,
                        FinanceEntryKind.ControlTotal,
                        FinanceEntryType.Payout,
                        forPay,
                        (forPay >= 0m) != isReturn
                            ? FinanceEntryDirection.Income
                            : FinanceEntryDirection.Expense);
                }

                continue;
            }

            decimal delivery = row.DeliveryService ?? 0m;

            if (delivery == 0m)
            {
                continue;
            }

            FinanceEntryDirection direction = delivery >= 0m
                ? FinanceEntryDirection.Expense
                : FinanceEntryDirection.Income;

            // The logistics row reports money leaving the seller. Without its own control total the
            // components would never reconcile with the payout, because forPay of the sale row does
            // not include the delivery fee.
            Emit(
                entries,
                row,
                reportInstanceKey,
                externalEntryId,
                externalProductId,
                currency,
                documentDate,
                periodFrom,
                periodTo,
                FinanceEntryKind.Component,
                FinanceEntryType.Logistics,
                delivery,
                direction);

            Emit(
                entries,
                row,
                reportInstanceKey,
                externalEntryId,
                externalProductId,
                currency,
                documentDate,
                periodFrom,
                periodTo,
                FinanceEntryKind.ControlTotal,
                FinanceEntryType.Payout,
                delivery,
                direction);
        }

        return new WildberriesFinanceMappingResult(entries, diagnostics);
    }

    private static bool TryResolveDocumentDate(
        WildberriesFinanceReportRow row,
        FinanceMappingDiagnostics diagnostics,
        out DateOnly documentDate)
    {
        if (TryParseDate(row.RrDate, out documentDate))
        {
            return true;
        }

        if (TryParseMoment(row.SaleDate, out documentDate))
        {
            diagnostics.RowsWithoutRrDate++;
            return true;
        }

        diagnostics.RowsWithoutRrDate++;
        return false;
    }

    private static void Emit(
        List<ChannelFinanceEntrySnapshot> entries,
        WildberriesFinanceReportRow row,
        string reportInstanceKey,
        string externalEntryId,
        string externalProductId,
        string currency,
        DateOnly documentDate,
        DateOnly periodFrom,
        DateOnly periodTo,
        FinanceEntryKind kind,
        FinanceEntryType type,
        decimal amount,
        FinanceEntryDirection direction)
    {
        entries.Add(new ChannelFinanceEntrySnapshot
        {
            Source = FinanceEntrySource.WbReport,
            ReportInstanceKey = reportInstanceKey,
            ExternalEntryId = externalEntryId,
            ExternalOrderId = null,
            ExternalLineId = row.Srid,
            ExternalProductId = externalProductId,
            Kind = kind,
            Type = type,
            Amount = Math.Abs(amount),
            Direction = direction,
            Currency = currency,
            DocumentDate = documentDate,
            PeriodFrom = periodFrom,
            PeriodTo = periodTo,
        });
    }

    private static bool TryParseDate(string? raw, out DateOnly date)
    {
        if (!string.IsNullOrWhiteSpace(raw)
            && DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        date = default;
        return false;
    }

    private static bool TryParseMoment(string? raw, out DateOnly date)
    {
        if (!string.IsNullOrWhiteSpace(raw)
            && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset moment))
        {
            date = DateOnly.FromDateTime(moment.UtcDateTime);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(raw)
            && DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
        {
            date = DateOnly.FromDateTime(parsed);
            return true;
        }

        date = default;
        return false;
    }

    /// <summary>
    /// Splits a sale or a return row into the seller's own components: what the buyer paid, the
    /// discount the marketplace financed, the commission charged on the seller's price and the
    /// acquiring fee. A return reports the same figures as the sale it reverses, so only the caller
    /// knows which way they move.
    /// </summary>
    /// <remarks>
    /// The split is taken only when it reproduces the channel's own payout to the kopeck. Both the
    /// rate and the discount are read from the row, so a row that does not add up — a changed tariff
    /// shape, an operation the report prices differently — is left to the reported composite instead
    /// of replacing revenue with an unverifiable figure. Only the magnitude of the payout is
    /// comparable: the report signs a return as a positive amount of money taken back, so its sign
    /// does not say whether the row adds to or subtracts from the seller's result.
    /// </remarks>
    private static bool TryMapSellerPriceSplit(
        WildberriesFinanceReportRow row,
        decimal retailAmount,
        bool reversal,
        out SaleSplit split)
    {
        split = default;

        decimal sellerPrice = Math.Abs(row.RetailPriceWithDisc ?? 0m);
        decimal buyerPaid = Math.Abs(retailAmount);
        decimal commissionPercent = row.CommissionPercent ?? 0m;

        if (sellerPrice <= 0m
            || buyerPaid <= 0m
            || sellerPrice < buyerPaid
            || commissionPercent <= 0m
            || commissionPercent >= 100m)
        {
            return false;
        }

        decimal compensation = sellerPrice - buyerPaid;
        decimal commission = decimal.Round(
            sellerPrice * commissionPercent / 100m,
            6,
            MidpointRounding.AwayFromZero);
        decimal acquiring = Math.Abs(row.AcquiringFee ?? 0m);

        decimal derived = buyerPaid + compensation - commission - acquiring;

        if (Math.Abs(derived - Math.Abs(row.ForPay ?? 0m)) > ControlTolerance)
        {
            return false;
        }

        split = new SaleSplit(buyerPaid, compensation, commission, acquiring);

        return true;
    }

    private readonly record struct SaleSplit(
        decimal BuyerPaid,
        decimal Compensation,
        decimal Commission,
        decimal Acquiring);
}

/// <summary>
/// The result of mapping one page of the Wildberries report.
/// </summary>
/// <param name="Entries">The finance entries of the page.</param>
/// <param name="Diagnostics">What the mapper skipped or had to fall back on.</param>
internal sealed record WildberriesFinanceMappingResult(
    IReadOnlyList<ChannelFinanceEntrySnapshot> Entries,
    FinanceMappingDiagnostics Diagnostics);
