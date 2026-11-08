using System.Globalization;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;

/// <summary>
/// Maps one page of Ozon posting list rows (<c>posting/fbo/list</c> or <c>posting/fbs/list</c>)
/// into <see cref="ChannelOrderSnapshot"/>. Pure mapping: no HTTP, no orchestration, no DI.
/// </summary>
internal static class OzonOrderMapper
{
    private const int MaxIdentityLength = 100;

    /// <summary>
    /// Prefix applied to every Ozon-reported fulfilment schema, keeping it distinct from the
    /// equivalent Wildberries codes (<c>wb_fbw</c>/<c>wb_fbs</c>) once both share one column.
    /// </summary>
    private const string OzonSchemaPrefix = "oz_";

    /// <summary>
    /// The raw <c>delivery_schema</c> value assumed for a posting read off
    /// <c>v3/posting/fbo/list</c> when the posting itself carries no such field.
    /// </summary>
    private const string FboEndpointRawSchema = "fbo";

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
    /// Maps one posting page, reporting tolerated anomalies to <paramref name="logger"/>.
    /// </summary>
    /// <param name="postings">The page's postings, in response order.</param>
    /// <param name="sourceSchema">The endpoint the page was read from, when known.</param>
    /// <param name="logger">Logger for unknown statuses and disagreeing currencies; may be null.</param>
    /// <returns>One snapshot per distinct order of the page, in first-arrival order, and the
    /// diagnostics collected while mapping it.</returns>
    public static OzonMappingResult Map(
        IReadOnlyList<OzonPosting> postings,
        OrdersSchema? sourceSchema,
        ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(postings);

        OzonMappingDiagnostics diagnostics = new OzonMappingDiagnostics();

        Dictionary<string, List<OzonPosting>> postingsByOrderKey = new Dictionary<string, List<OzonPosting>>(StringComparer.Ordinal);
        List<string> orderKeysInArrivalOrder = new List<string>();

        foreach (OzonPosting posting in postings)
        {
            if (string.IsNullOrWhiteSpace(posting.PostingNumber))
            {
                diagnostics.ReportRowsWithoutIdentity(1);
                continue;
            }

            string? orderKey = GetOrderKey(posting);

            if (orderKey == null)
            {
                diagnostics.ReportRowsWithoutIdentity(1);
                continue;
            }

            if (posting.Products == null || posting.Products.Count == 0)
            {
                diagnostics.ReportRowsWithoutIdentity(1);
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

        List<ChannelOrderSnapshot> snapshots = new List<ChannelOrderSnapshot>(orderKeysInArrivalOrder.Count);

        foreach (string orderKey in orderKeysInArrivalOrder)
        {
            snapshots.Add(MapToOrderSnapshot(
                orderKey,
                postingsByOrderKey[orderKey],
                sourceSchema,
                logger,
                diagnostics));
        }

        return new OzonMappingResult(snapshots, diagnostics.RowsWithoutIdentity);
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
        string orderKey,
        List<OzonPosting> orderPostings,
        OrdersSchema? sourceSchema,
        ILogger? logger,
        OzonMappingDiagnostics diagnostics)
    {
        EnsureIdentityLength(orderKey, nameof(orderKey));

        List<ChannelOrderLineSnapshot> lines = new List<ChannelOrderLineSnapshot>();
        List<DateTimeOffset> rowOrderDates = new List<DateTimeOffset>();

        foreach (OzonPosting posting in orderPostings)
        {
            DateTimeOffset? dateCandidate = posting.CreatedAt ?? posting.InProcessAt;

            if (dateCandidate.HasValue)
            {
                rowOrderDates.Add(dateCandidate.Value);
            }

            foreach (ChannelOrderLineSnapshot line in MapPostingLines(
                posting, sourceSchema, logger, diagnostics))
            {
                lines.Add(line);
            }
        }

        if (rowOrderDates.Count == 0)
        {
            throw new InvalidOperationException(
                $"Ozon order '{orderKey}' has no created_at or in_process_at; " +
                "the order carries no placement instant and one cannot be invented.");
        }

        bool allCancelled = orderPostings.TrueForAll(IsCancelledPosting);

        OzonPosting determiningPosting = allCancelled
            ? orderPostings[0]
            : SelectHeaderWinner(orderPostings, logger, diagnostics);

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
            DeliveryCity = firstPosting.AnalyticsData?.City ?? firstPosting.FinancialData?.ClusterTo,
            DeliveryRegion = firstPosting.AnalyticsData?.Region ?? firstPosting.FinancialData?.ClusterFrom,
            WarehouseName = firstPosting.AnalyticsData?.WarehouseName ?? firstPosting.DeliveryMethod?.Warehouse,
            IsB2b = firstPosting.AnalyticsData?.IsLegal,
            IsTestOrder = null,
            ChannelReportedTotal = header.ChannelReportedTotal,
            ChannelReportedDeliveryFee = null,
            Lines = lines,
            ReportsHeaderStatus = true
        };
    }

    private static List<ChannelOrderLineSnapshot> MapPostingLines(
        OzonPosting posting, OrdersSchema? sourceSchema, ILogger? logger, OzonMappingDiagnostics diagnostics)
    {
        List<OzonPostingProduct> products = posting.Products!;
        List<ChannelOrderLineSnapshot> lines = new List<ChannelOrderLineSnapshot>(products.Count);

        Dictionary<long, int> occurrencesBySku = new Dictionary<long, int>();

        foreach (OzonPostingProduct product in products)
        {
            int occurrenceCount;
            occurrencesBySku[product.Sku] = occurrencesBySku.TryGetValue(product.Sku, out occurrenceCount)
                ? occurrenceCount + 1
                : 1;
        }

        string? fulfillmentSchema = ResolveFulfillmentSchema(posting, sourceSchema, logger, diagnostics);

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
                UnitPrice = product.Price?.Amount,
                BuyerUnitPrice = financialProduct?.CustomerPrice?.Amount,
                UnitPriceBeforeDiscount = financialProduct?.OldPrice,
                Currency = product.Price?.Currency ?? string.Empty,
                IsCancelled = IsCancelledPosting(posting),
                StatusChangedAt = posting.InProcessAt ?? posting.CreatedAt,
                ChannelReportedTotal = null,
                ChannelReportedCommission = financialProduct?.Commission?.Amount,
                ChannelReportedPayout = financialProduct?.Payout,
                FulfillmentSchema = fulfillmentSchema
            });
        }

        return lines;
    }

    /// <summary>
    /// Resolves one posting's <c>FulfillmentSchema</c>, normalized to Ozon's prefixed codes
    /// (<c>oz_fbo</c>/<c>oz_fbs</c>/<c>oz_rfbs</c>). The posting's own <c>delivery_schema</c>
    /// wins whenever it reports one - that covers FBS/rFBS, and tolerates Ozon adding the field to
    /// the FBO endpoint later. Absent that, a posting known to come from
    /// <see cref="OrdersSchema.Fbo"/> defaults to <c>oz_fbo</c>: that endpoint's postings are, by
    /// construction, exclusively Ozon-fulfilled, so the default is not a guess but a fact about the
    /// endpoint the mapper's caller already knows. A posting from the FBS endpoint that still omits
    /// the field, or one whose source endpoint is unknown, resolves to null instead of being
    /// defaulted the other way - rFBS exists and must not be folded into "fbs".
    /// </summary>
    private static string? ResolveFulfillmentSchema(
        OzonPosting posting, OrdersSchema? sourceSchema, ILogger? logger, OzonMappingDiagnostics diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(posting.DeliverySchema))
        {
            return NormalizeOzonSchema(posting.DeliverySchema);
        }

        if (sourceSchema == OrdersSchema.Fbo)
        {
            return NormalizeOzonSchema(FboEndpointRawSchema);
        }

        if (sourceSchema == OrdersSchema.Fbs && diagnostics.TryReportMissingFbsSchema())
        {
            logger?.LogWarning(
                "Ozon FBS posting {PostingNumber} did not report delivery_schema; " +
                "FulfillmentSchema is left unresolved for it.",
                posting.PostingNumber);
        }

        return null;
    }

    /// <summary>
    /// Prefixes a raw Ozon <c>delivery_schema</c> value (<c>fbo</c>/<c>fbs</c>/<c>rfbs</c>, as
    /// Ozon itself reports it) so it cannot be confused with Wildberries' own schema codes once
    /// both channels write into the same <see cref="ChannelOrderLineSnapshot.FulfillmentSchema"/>
    /// column. Applied uniformly rather than through a lookup table, so a schema Ozon adds later
    /// still comes out prefixed instead of falling through unnoticed.
    /// </summary>
    private static string NormalizeOzonSchema(string rawSchema)
    {
        return OzonSchemaPrefix + rawSchema.ToLowerInvariant();
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

        int previousOrdinal;
        int ordinal = ordinalBySku.TryGetValue(sku, out previousOrdinal) ? previousOrdinal + 1 : 1;
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
        List<OzonPosting> orderPostings, ILogger? logger, OzonMappingDiagnostics diagnostics)
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

            int knownRank;
            if (posting.Status != null && StatusRank.TryGetValue(posting.Status, out knownRank))
            {
                rank = knownRank;
            }
            else
            {
                rank = int.MaxValue;

                if (diagnostics.TryReportUnknownStatus())
                {
                    logger?.LogWarning(
                        "Ozon posting {PostingNumber} reports unrecognized status {Status}; it ranks " +
                        "last in header aggregation.",
                        posting.PostingNumber,
                        posting.Status);
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

    private static void EnsureIdentityLength(string identity, string name)
    {
        if (identity.Length > MaxIdentityLength)
        {
            throw new InvalidOperationException(
                $"Ozon computed {name} '{identity}' exceeds the maximum persistable length of {MaxIdentityLength} characters.");
        }
    }
}

internal sealed class OzonMappingDiagnostics
{
    private bool _loggedUnknownStatus;
    private bool _loggedMissingFbsSchema;

    public int RowsWithoutIdentity { get; private set; }

    public void ReportRowsWithoutIdentity(int count)
    {
        RowsWithoutIdentity += count;
    }

    /// <summary>
    /// Returns true exactly once, so the caller logs the first occurrence only.
    /// </summary>
    public bool TryReportUnknownStatus()
    {
        if (_loggedUnknownStatus)
        {
            return false;
        }

        _loggedUnknownStatus = true;
        return true;
    }

    /// <summary>
    /// Returns true exactly once, so the caller logs the first occurrence only.
    /// </summary>
    public bool TryReportMissingFbsSchema()
    {
        if (_loggedMissingFbsSchema)
        {
            return false;
        }

        _loggedMissingFbsSchema = true;
        return true;
    }
}

internal sealed record OzonMappingResult(List<ChannelOrderSnapshot> Orders, int RowsWithoutIdentity);
