using System.Globalization;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;

/// <summary>
/// Maps one page of Ozon FBO <c>posting/fbo/list</c> rows into <see cref="ChannelOrderSnapshot"/>.
/// Pure mapping: no HTTP, no orchestration, no DI.
/// </summary>
internal static class OzonOrderMapper
{
    private const int MaxIdentityLength = 100;

    private static readonly IReadOnlyDictionary<string, int> StatusRank =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["awaiting_registration"] = 0,
            ["awaiting_approve"] = 1,
            ["awaiting_packaging"] = 2,
            ["acceptance_in_progress"] = 3,
            ["awaiting_deliver"] = 4,
            ["driver_pickup"] = 5,
            ["sent_by_seller"] = 6,
            ["delivering"] = 7,
            ["delivered"] = 8
        };

    /// <summary>
    /// Maps one posting page without diagnostics. Prefer the overload taking an
    /// <see cref="ILogger"/> where one is available: the anomalies this mapper tolerates (unknown
    /// status, disagreeing currencies) are only ever observable through it.
    /// </summary>
    /// <param name="postings">The page's postings, in response order.</param>
    /// <param name="rowsWithoutIdentity">The number of postings dropped for lacking an identity.</param>
    /// <returns>One snapshot per distinct order of the page, in first-arrival order.</returns>
    public static List<ChannelOrderSnapshot> Map(IReadOnlyList<OzonPosting> postings, out int rowsWithoutIdentity)
    {
        return Map(postings, null, out rowsWithoutIdentity);
    }

    /// <summary>
    /// Maps one posting page, reporting tolerated anomalies to <paramref name="logger"/>.
    /// </summary>
    /// <param name="postings">The page's postings, in response order.</param>
    /// <param name="logger">Logger for unknown statuses and disagreeing currencies; may be null.</param>
    /// <param name="rowsWithoutIdentity">The number of postings dropped for lacking an identity.</param>
    /// <returns>One snapshot per distinct order of the page, in first-arrival order.</returns>
    public static List<ChannelOrderSnapshot> Map(
        IReadOnlyList<OzonPosting> postings, ILogger? logger, out int rowsWithoutIdentity)
    {
        ArgumentNullException.ThrowIfNull(postings);

        rowsWithoutIdentity = 0;

        Dictionary<string, List<OzonPosting>> postingsByOrderKey = new Dictionary<string, List<OzonPosting>>(StringComparer.Ordinal);
        List<string> orderKeysInArrivalOrder = new List<string>();

        foreach (OzonPosting posting in postings)
        {
            if (string.IsNullOrWhiteSpace(posting.PostingNumber))
            {
                rowsWithoutIdentity++;
                continue;
            }

            string? orderKey = GetOrderKey(posting);

            if (orderKey == null)
            {
                rowsWithoutIdentity++;
                continue;
            }

            if (posting.Products == null || posting.Products.Count == 0)
            {
                rowsWithoutIdentity++;
                continue;
            }

            if (!postingsByOrderKey.TryGetValue(orderKey, out List<OzonPosting>? orderPostings))
            {
                orderPostings = new List<OzonPosting>();
                postingsByOrderKey.Add(orderKey, orderPostings);
                orderKeysInArrivalOrder.Add(orderKey);
            }

            orderPostings.Add(posting);
        }

        bool loggedUnknownStatus = false;
        List<ChannelOrderSnapshot> snapshots = new List<ChannelOrderSnapshot>(orderKeysInArrivalOrder.Count);

        foreach (string orderKey in orderKeysInArrivalOrder)
        {
            snapshots.Add(MapToOrderSnapshot(orderKey, postingsByOrderKey[orderKey], logger, ref loggedUnknownStatus));
        }

        return snapshots;
    }

    private static string? GetOrderKey(OzonPosting posting)
    {
        if (!string.IsNullOrWhiteSpace(posting.OrderNumber))
        {
            return posting.OrderNumber;
        }

        if (posting.OrderId is long orderId and not 0)
        {
            return orderId.ToString(CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static ChannelOrderSnapshot MapToOrderSnapshot(
        string orderKey, List<OzonPosting> orderPostings, ILogger? logger, ref bool loggedUnknownStatus)
    {
        EnsureIdentityLength(orderKey, nameof(orderKey));

        List<ChannelOrderLineSnapshot> lines = new List<ChannelOrderLineSnapshot>();
        List<DateTimeOffset> rowOrderDates = new List<DateTimeOffset>();

        foreach (OzonPosting posting in orderPostings)
        {
            if (posting.CreatedAt is DateTimeOffset createdAt)
            {
                rowOrderDates.Add(createdAt);
            }

            foreach (ChannelOrderLineSnapshot line in MapPostingLines(posting))
            {
                lines.Add(line);
            }
        }

        if (rowOrderDates.Count == 0)
        {
            throw new InvalidOperationException(
                $"Ozon order '{orderKey}' reports no posting with a created_at; " +
                "the order carries no placement instant and one cannot be invented.");
        }

        bool allCancelled = orderPostings.TrueForAll(IsCancelledPosting);

        OzonPosting determiningPosting = allCancelled
            ? orderPostings[0]
            : SelectHeaderWinner(orderPostings, logger, ref loggedUnknownStatus);

        string? reportedHeaderStatus = allCancelled ? null : determiningPosting.Status;
        string? reportedCancelReason = allCancelled ? GetCancelReason(determiningPosting) : null;

        ChannelOrderHeaderFacts header =
            ChannelOrderHeaderFacts.Reduce(lines, rowOrderDates, reportedHeaderStatus, reportedCancelReason);

        OzonPosting firstPosting = orderPostings[0];
        string currency = lines[0].Currency;

        for (int i = 1; i < lines.Count; i++)
        {
            if (!string.Equals(lines[i].Currency, currency, StringComparison.Ordinal))
            {
                logger?.LogWarning(
                    "Ozon order {OrderKey} reports lines with disagreeing currencies; the first " +
                    "line's currency {Currency} wins.",
                    orderKey,
                    currency);
                break;
            }
        }

        return new ChannelOrderSnapshot
        {
            ExternalOrderId = orderKey,
            ExternalOrderNumber = orderKey,
            OrderDate = header.OrderDate,
            StatusChangedAt = header.StatusChangedAt,
            ExternalStatus = header.ExternalStatus,
            ExternalSubstatus = determiningPosting.Substatus,
            CancelReason = header.CancelReason,
            Currency = currency,
            DeliveryCity = firstPosting.AnalyticsData?.City,
            DeliveryRegion = firstPosting.AnalyticsData?.Region,
            WarehouseName = firstPosting.AnalyticsData?.WarehouseName,
            IsSellerWarehouse = false,
            IsB2b = firstPosting.AnalyticsData?.IsLegal,
            IsTestOrder = null,
            ChannelReportedTotal = header.ChannelReportedTotal,
            ChannelReportedDeliveryFee = null,
            Lines = lines,
            ReportsHeaderStatus = true
        };
    }

    private static List<ChannelOrderLineSnapshot> MapPostingLines(OzonPosting posting)
    {
        List<OzonPostingProduct> products = posting.Products!;
        List<ChannelOrderLineSnapshot> lines = new List<ChannelOrderLineSnapshot>(products.Count);

        Dictionary<long, int> occurrencesBySku = new Dictionary<long, int>();

        foreach (OzonPostingProduct product in products)
        {
            occurrencesBySku[product.Sku] = occurrencesBySku.TryGetValue(product.Sku, out int count) ? count + 1 : 1;
        }

        Dictionary<long, int> ordinalBySku = new Dictionary<long, int>();
        OzonPostingFinancialProduct? financialProduct;

        foreach (OzonPostingProduct product in products)
        {
            string lineKey = BuildLineKey(posting.PostingNumber!, product.Sku, products.Count, occurrencesBySku, ordinalBySku);
            EnsureIdentityLength(lineKey, nameof(lineKey));

            financialProduct = FindFinancialProduct(posting.FinancialData, product.Sku);

            lines.Add(new ChannelOrderLineSnapshot
            {
                LineKey = lineKey,
                ExternalLineId = posting.PostingNumber,
                ExternalProductId = product.Sku.ToString(CultureInfo.InvariantCulture),
                OfferId = product.OfferId,
                ExternalSizeId = null,
                ProductName = product.Name,
                Vat = null,
                Quantity = product.Quantity,
                UnitPrice = ParseDecimal(product.Price?.Amount),
                BuyerUnitPrice = ParseDecimal(financialProduct?.CustomerPrice?.Amount),
                UnitPriceBeforeDiscount = ParseDecimal(financialProduct?.OldPrice),
                Currency = product.Price?.Currency ?? string.Empty,
                IsCancelled = IsCancelledPosting(posting),
                StatusChangedAt = posting.InProcessAt ?? posting.CreatedAt,
                ChannelReportedTotal = null,
                ChannelReportedCommission = ParseDecimal(financialProduct?.Commission?.Amount),
                ChannelReportedPayout = ParseDecimal(financialProduct?.Payout)
            });
        }

        return lines;
    }

    private static string BuildLineKey(
        string postingNumber,
        long sku,
        int productCount,
        Dictionary<long, int> occurrencesBySku,
        Dictionary<long, int> ordinalBySku)
    {
        if (productCount == 1)
        {
            return postingNumber;
        }

        if (occurrencesBySku[sku] == 1)
        {
            return $"{postingNumber}:{sku.ToString(CultureInfo.InvariantCulture)}";
        }

        int ordinal = ordinalBySku.TryGetValue(sku, out int previous) ? previous + 1 : 1;
        ordinalBySku[sku] = ordinal;

        return $"{postingNumber}:{sku.ToString(CultureInfo.InvariantCulture)}:{ordinal.ToString(CultureInfo.InvariantCulture)}";
    }

    private static OzonPostingFinancialProduct? FindFinancialProduct(OzonPostingFinancialData? financialData, long sku)
    {
        if (financialData?.Products == null)
        {
            return null;
        }

        foreach (OzonPostingFinancialProduct financialProduct in financialData.Products)
        {
            if (financialProduct.ProductId == sku)
            {
                return financialProduct;
            }
        }

        return null;
    }

    private static bool IsCancelledPosting(OzonPosting posting)
    {
        return string.Equals(posting.Status, "cancelled", StringComparison.OrdinalIgnoreCase);
    }

    private static OzonPosting SelectHeaderWinner(
        List<OzonPosting> orderPostings, ILogger? logger, ref bool loggedUnknownStatus)
    {
        OzonPosting? winner = null;
        int winnerRank = int.MaxValue;

        foreach (OzonPosting posting in orderPostings)
        {
            if (IsCancelledPosting(posting))
            {
                continue;
            }

            int rank;

            if (posting.Status != null && StatusRank.TryGetValue(posting.Status, out int knownRank))
            {
                rank = knownRank;
            }
            else
            {
                rank = int.MaxValue;

                if (!loggedUnknownStatus)
                {
                    logger?.LogWarning(
                        "Ozon posting {PostingNumber} reports unrecognized status {Status}; it ranks " +
                        "last in header aggregation.",
                        posting.PostingNumber,
                        posting.Status);
                    loggedUnknownStatus = true;
                }
            }

            if (winner == null || rank < winnerRank)
            {
                winner = posting;
                winnerRank = rank;
            }
        }

        return winner ?? orderPostings[0];
    }

    private static string? GetCancelReason(OzonPosting posting)
    {
        if (!string.IsNullOrWhiteSpace(posting.Cancellation?.CancelReason))
        {
            return posting.Cancellation!.CancelReason;
        }

        long? cancelReasonId = posting.Cancellation?.CancelReasonId ?? posting.CancelReasonId;

        if (cancelReasonId is long id and not 0)
        {
            return id.ToString(CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static decimal? ParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
            ? value
            : null;
    }

    private static void EnsureIdentityLength(string identity, string name)
    {
        if (identity.Length > MaxIdentityLength)
        {
            throw new InvalidOperationException(
                $"Ozon computed {name} '{identity}' exceeds the maximum persistable length of {MaxIdentityLength} characters.");
        }
    }
}
