using System.Globalization;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Orders;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Wildberries.Dtos;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Wildberries;

/// <summary>
/// Maps one page of Wildberries <c>/api/v1/supplier/orders</c> rows (one ordered unit per row)
/// into <see cref="ChannelOrderSnapshot"/>. Pure mapping: no HTTP, no orchestration, no DI.
/// </summary>
internal static class WildberriesOrderMapper
{
    private const string WildberriesCurrency = "RUB";

    /// <summary>
    /// Longest <c>gNumber</c> or <c>srid</c>/sticker identity this mapper accepts.
    /// </summary>
    private const int MaxIdentityLength = 100;

    private const string StickerIdentityPrefix = "wb-stk:";
    private const string WildberriesSellerWarehouseSchema = "wb_fbs";
    private const string WildberriesOwnWarehouseSchema = "wb_fbw";

    /// <summary>
    /// Wildberries has operated on a fixed UTC+3 offset (no DST) since 2014; the orders API
    /// takes dates in this local time and does not accept a time zone designator.
    /// </summary>
    private static readonly TimeSpan MoscowOffset = OrdersWindowPlanner.MoscowTime;

    /// <summary>
    /// Groups one response's rows into carts and maps each cart to a snapshot.
    /// </summary>
    /// <param name="orders">The page's rows, in response order.</param>
    /// <param name="request">Request the page answers.</param>
    /// <param name="maxOrdersPerResponse">
    /// The page cap the caller applies: a response holding that many rows is treated as capped and
    /// may therefore cover only part of the requested window.
    /// </param>
    /// <param name="logger">Logger for rows without identity; may be null.</param>
    /// <returns>One snapshot per distinct cart, in first-arrival order, plus the page's coverage.</returns>
    public static WildberriesOrderMappingResult Map(
        IReadOnlyList<WildberriesOrderDto> orders,
        OrdersRequest request,
        int maxOrdersPerResponse,
        ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(request);

        DateTimeOffset dateToMsk = request.DateTo.ToOffset(MoscowOffset);

        List<ChannelOrderSnapshot> snapshots = new List<ChannelOrderSnapshot>();
        Dictionary<string, List<WildberriesOrderDto>> rowsByOrderKey = new Dictionary<string, List<WildberriesOrderDto>>(StringComparer.Ordinal);

        List<string> orderKeysInArrivalOrder = new List<string>();

        int rowsWithoutIdentity = 0;

        DateTimeOffset? coveredUntil = null;
        DateTimeOffset? previousSelectionInstant = null;

        bool coversContiguousPrefix = true;

        foreach (WildberriesOrderDto order in orders)
        {
            DateTimeOffset? selectionInstant = GetSelectionInstant(order, request.Selection);

            if (selectionInstant != null)
            {
                if (previousSelectionInstant != null && selectionInstant.Value < previousSelectionInstant.Value)
                {
                    coversContiguousPrefix = false;
                }

                previousSelectionInstant = selectionInstant;

                if (coveredUntil == null || selectionInstant.Value > coveredUntil.Value)
                {
                    coveredUntil = selectionInstant.Value;
                }
            }

            if (!IsWithinRequestedWindow(order, request.Selection, dateToMsk))
            {
                continue;
            }

            string? lineKey = GetLineKey(order);

            if (lineKey == null)
            {
                // Never fabricate a key here: any composite over nmId, techSize, gNumber and date
                // is not proven unique per order (one cart may hold two units of the same article
                // and size, and Wildberries reports date to the second), so resolving the
                // collision would merge two real orders into one and report the loss as a
                // successful import. The row is dropped and counted instead.
                rowsWithoutIdentity++;

                logger?.LogError(
                    "Wildberries order row without usable identity skipped: NmId={NmId}, GNumber={GNumber}, Date={Date}, Sticker={Sticker}.",
                    order.NmId,
                    order.GNumber,
                    order.Date,
                    order.Sticker);

                continue;
            }

            EnsureIdentityLength(lineKey, nameof(lineKey));

            string? orderKey = GetOrderKey(order);

            if (orderKey == null)
            {
                // Distinct from the missing line identity above: this row does have a line identity,
                // it lacks the order identity that would place it in a cart. Conflating the two is
                // what let a cart be split into one order per unit.
                rowsWithoutIdentity++;

                logger?.LogError(
                    "Wildberries order row without a gNumber skipped: NmId={NmId}, Srid={Srid}, Date={Date}, Sticker={Sticker}.",
                    order.NmId,
                    order.Srid,
                    order.Date,
                    order.Sticker);

                continue;
            }

            EnsureIdentityLength(orderKey, nameof(orderKey));

            if (!rowsByOrderKey.TryGetValue(orderKey, out List<WildberriesOrderDto>? rows))
            {
                rows = new List<WildberriesOrderDto>();
                rowsByOrderKey.Add(orderKey, rows);
                orderKeysInArrivalOrder.Add(orderKey);
            }
            else if (ContainsLineKey(rows, lineKey))
            {
                // Unlike a repeated order key, a repeated line key inside one order cannot describe
                // the response: one srid is one ordered unit, so two rows claiming it are two
                // versions of that unit rather than two units. Keeping either would silently decide
                // which reported row the import believes.
                throw new InvalidOperationException(
                    $"Wildberries returned line key '{lineKey}' more than once within order '{orderKey}' in one response.");
            }

            rows.Add(order);
        }

        foreach (string orderKey in orderKeysInArrivalOrder)
        {
            snapshots.Add(MapToOrderSnapshot(orderKey, rowsByOrderKey[orderKey]));
        }

        bool hasMore = maxOrdersPerResponse > 0 && orders.Count >= maxOrdersPerResponse;

        // A response under the cap came back whole, so it covers the window end that was asked
        // for. A capped one covers only as far as its last returned selection instant, and only
        // when its rows arrived ordered by that instant.
        DateTimeOffset? resultCoveredUntil = !hasMore
            ? request.DateTo
            : coversContiguousPrefix ? coveredUntil : null;

        return new WildberriesOrderMappingResult(
            snapshots,
            hasMore,
            resultCoveredUntil,
            rowsWithoutIdentity);
    }

    /// <summary>
    /// Resolves the identity of one ordered unit. <c>srid</c> is the documented per-order id and is
    /// returned unmodified; <c>sticker</c> is the only other field the payload guarantees one of
    /// per ordered unit, and is returned with a prefix so it stays recognisable as a derived identity.
    /// </summary>
    /// <remarks>
    /// The result is a line identity, not an order identity: it is unique within the owning order,
    /// and two carts may carry the same <c>srid</c>.
    /// </remarks>
    private static string? GetLineKey(WildberriesOrderDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.Srid))
        {
            return dto.Srid;
        }

        if (!string.IsNullOrWhiteSpace(dto.Sticker))
        {
            return StickerIdentityPrefix + dto.Sticker;
        }

        return null;
    }

    /// <summary>
    /// Resolves the identity of one cart. <c>gNumber</c> is the only field that groups the units of
    /// one cart and the only one that is immutable, so it is the sole source of the order key; a row
    /// that does not report one has no placeable order identity and yields null.
    /// </summary>
    private static string? GetOrderKey(WildberriesOrderDto dto)
    {
        return string.IsNullOrWhiteSpace(dto.GNumber) ? null : dto.GNumber;
    }

    private static void EnsureIdentityLength(string identity, string name)
    {
        if (identity.Length > MaxIdentityLength)
        {
            throw new InvalidOperationException(
                $"Wildberries computed {name} '{identity}' exceeds the maximum persistable length of {MaxIdentityLength} characters.");
        }
    }

    private static bool ContainsLineKey(List<WildberriesOrderDto> rows, string lineKey)
    {
        foreach (WildberriesOrderDto row in rows)
        {
            if (string.Equals(GetLineKey(row), lineKey, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static ChannelOrderSnapshot MapToOrderSnapshot(string orderKey, List<WildberriesOrderDto> rows)
    {
        WildberriesOrderDto first = rows[0];

        List<ChannelOrderLineSnapshot> lines = new List<ChannelOrderLineSnapshot>(rows.Count);
        List<DateTimeOffset> rowOrderDates = new List<DateTimeOffset>(rows.Count);

        foreach (WildberriesOrderDto row in rows)
        {
            string lineKey = GetLineKey(row)
                ?? throw new InvalidOperationException(
                    $"Wildberries order '{orderKey}' contains a row without a usable line identity.");

            lines.Add(MapToOrderLine(row, lineKey));

            DateTimeOffset? rowOrderDate = ParseMoscowTimestamp(row.Date);

            if (rowOrderDate != null)
            {
                rowOrderDates.Add(rowOrderDate.Value);
            }
        }

        if (rowOrderDates.Count == 0)
        {
            // Substituting the wall clock here would make every refetch of an unchanged cart differ,
            // and the difference would surface upstream as an update on every run.
            throw new InvalidOperationException(
                $"Wildberries order '{orderKey}' reports no row with a parseable date; " +
                "the cart carries no placement instant and one cannot be invented.");
        }

        // Every cart-level field below is reduced from the lines and from the rows' dates by this
        // one helper; nothing else in the codebase may compute them.
        ChannelOrderHeaderFacts header = ChannelOrderHeaderFacts.Reduce(lines, rowOrderDates);

        return new ChannelOrderSnapshot
        {
            ExternalOrderId = orderKey,

            // gNumber is the buyer-facing order number, so it is both the identity and the number;
            // reporting it twice is what the contract asks for.
            ExternalOrderNumber = orderKey,
            OrderDate = header.OrderDate,
            StatusChangedAt = header.StatusChangedAt,
            ExternalStatus = header.ExternalStatus,

            // Reported per unit with no cart-level counterpart, so the first row stands in for the
            // cart instead of a merge rule the payload does not define.
            ExternalSubstatus = first.OrderType,
            CancelReason = header.CancelReason,
            Currency = WildberriesCurrency,
            DeliveryRegion = first.RegionName,
            WarehouseName = first.WarehouseName,
            ChannelReportedTotal = header.ChannelReportedTotal,
            Lines = lines
        };
    }

    private static ChannelOrderLineSnapshot MapToOrderLine(WildberriesOrderDto dto, string lineKey)
    {
        return new ChannelOrderLineSnapshot
        {
            LineKey = lineKey,
            ExternalLineId = dto.Srid,
            ExternalProductId = dto.NmId.ToString(CultureInfo.InvariantCulture),
            OfferId = dto.SupplierArticle,
            ExternalSizeId = dto.TechSize,

            // supplier/orders reports only `subject`, the product's category, never its card title.
            // Writing the category here would publish a wrong fact that no consumer can tell apart
            // from a real name; the name is resolved on read from ChannelProductRef instead.
            ProductName = null,
            Quantity = 1,
            UnitPrice = dto.PriceWithDisc,
            BuyerUnitPrice = dto.FinishedPrice,
            UnitPriceBeforeDiscount = dto.TotalPrice,
            Currency = WildberriesCurrency,
            IsCancelled = dto.IsCancel,
            StatusChangedAt = ParseMoscowTimestamp(dto.LastChangeDate),
            ChannelReportedTotal = dto.TotalPrice,
            FulfillmentSchema = ResolveFulfillmentSchema(dto.WarehouseType)
        };
    }

    private static string? ResolveFulfillmentSchema(string? warehouseType)
    {
        if (string.IsNullOrWhiteSpace(warehouseType))
        {
            return null;
        }

        return warehouseType.Contains("продавца", StringComparison.OrdinalIgnoreCase)
            ? WildberriesSellerWarehouseSchema
            : WildberriesOwnWarehouseSchema;
    }

    private static DateTimeOffset? GetSelectionInstant(WildberriesOrderDto dto, OrdersSelectionMode selection)
    {
        return selection == OrdersSelectionMode.LastChanged
            ? ParseMoscowTimestamp(dto.LastChangeDate) ?? ParseMoscowTimestamp(dto.Date)
            : ParseMoscowTimestamp(dto.Date);
    }

    private static bool IsWithinRequestedWindow(
        WildberriesOrderDto dto, OrdersSelectionMode selection, DateTimeOffset dateToMsk)
    {
        DateTimeOffset? reference = GetSelectionInstant(dto, selection);

        return reference is null || reference.Value <= dateToMsk;
    }

    /// <summary>
    /// Wildberries reports timestamps as local (MSK) time without an offset designator; parsing
    /// them as UTC or local-to-this-process would silently shift them by an arbitrary amount.
    /// </summary>
    private static DateTimeOffset? ParseMoscowTimestamp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local))
        {
            return null;
        }

        return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), MoscowOffset)
            .ToUniversalTime();
    }
}

/// <summary>
/// One page of Wildberries orders after mapping, with the coverage facts the mapper derived.
/// </summary>
internal sealed record WildberriesOrderMappingResult(
    List<ChannelOrderSnapshot> Orders,
    bool HasMore,
    DateTimeOffset? CoveredUntil,
    int RowsWithoutIdentity);
