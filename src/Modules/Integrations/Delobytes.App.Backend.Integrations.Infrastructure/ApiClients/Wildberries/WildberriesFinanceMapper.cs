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
    private const string LogisticsOperation = "Логистика";

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
            if (!string.Equals(row.SellerOperName, SaleOperation, StringComparison.Ordinal)
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

            if (string.Equals(row.SellerOperName, SaleOperation, StringComparison.Ordinal))
            {
                decimal retail = row.RetailAmount ?? 0m;

                if (retail != 0m)
                {
                    if (retail < 0m)
                    {
                        diagnostics.Reversals++;
                    }

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
                        retail >= 0m ? FinanceEntryDirection.Income : FinanceEntryDirection.Expense);
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
                        acquiring >= 0m ? FinanceEntryDirection.Expense : FinanceEntryDirection.Income);
                }

                // The commission is recorded exactly as reported: a signed composite of vw, its VAT
                // and the reward. Deriving it from the sale price drifts as soon as the channel
                // changes a rate and makes the control total stop reconciling.
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
                        commission >= 0m ? FinanceEntryDirection.Expense : FinanceEntryDirection.Income);
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
                        forPay >= 0m ? FinanceEntryDirection.Income : FinanceEntryDirection.Expense);
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
}

/// <summary>
/// The result of mapping one page of the Wildberries report.
/// </summary>
/// <param name="Entries">The finance entries of the page.</param>
/// <param name="Diagnostics">What the mapper skipped or had to fall back on.</param>
internal sealed record WildberriesFinanceMappingResult(
    IReadOnlyList<ChannelFinanceEntrySnapshot> Entries,
    FinanceMappingDiagnostics Diagnostics);
