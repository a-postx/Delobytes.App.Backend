using System.Globalization;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Orders;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;

/// <summary>
/// Builds Ozon request bodies. Pure: no limiter, no logger, no binding state.
/// </summary>
internal static class OzonRequestFactory
{
    // Both posting list endpoints share the same page size envelope.
    private const int PostingListPageSizeMinimum = 1;
    private const int PostingListPageSizeMaximum = 100;
    private const int PostingListDefaultPageSize = 100;

    public static OzonPostingListRequest BuildFboBody(OrdersRequest request, int limit)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new OzonPostingListRequest
        {
            Cursor = request.Cursor ?? string.Empty,
            Filter = new OzonPostingListFilter
            {
                Since = FormatUtc(request.DateFrom),
                To = FormatUtc(request.DateTo),
                Statuses = request.Statuses is { Count: > 0 }
                    ? new List<string>(request.Statuses)
                    : new List<string>(OzonPostingStatuses.FboStatuses),
            },
            Limit = limit,
            SortDir = "ASC",
            Translit = false,
            With = new OzonPostingListWith
            {
                AnalyticsData = true,
                FinancialData = true,
                LegalInfo = false,
            },
        };
    }

    /// <summary>
    /// Builds the FBS <c>posting/fbs/list</c> body. Separate from <see cref="BuildFboBody"/> because
    /// the endpoint takes a different DTO: its filter carries the required <c>since</c>/<c>to</c> pair
    /// and its <c>with</c> block has no analytics section.
    /// </summary>
    public static OzonFbsPostingListRequest BuildFbsBody(OrdersRequest request, int limit)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new OzonFbsPostingListRequest
        {
            Cursor = request.Cursor ?? string.Empty,
            Filter = new OzonFbsPostingListFilter
            {
                Since = FormatUtc(request.DateFrom),
                To = FormatUtc(request.DateTo),

                // The full set is sent rather than null: the filter member is declared nullable, so a
                // null would serialize as an explicit "statuses": null instead of being omitted, and
                // the endpoint is only documented to drop the filter when the member is absent.
                Statuses = request.Statuses is { Count: > 0 }
                    ? new List<string>(request.Statuses)
                    : new List<string>(OzonPostingStatuses.FbsStatuses),
            },
            Limit = limit,
            SortDir = "asc",
            With = new OzonFbsPostingListWith
            {
                FinancialData = true,
            },
        };
    }

    public static OzonProductAttributesRequest BuildProductAttributesBody(int pageSize, string? cursorToken)
    {
        return new OzonProductAttributesRequest
        {
            Filter = new OzonProductAttributesFilter(),
            Limit = pageSize,
            LastId = string.IsNullOrEmpty(cursorToken) ? null : cursorToken,
        };
    }

    public static int ClampPostingLimit(int limit)
    {
        if (limit <= 0)
        {
            return PostingListDefaultPageSize;
        }

        return Math.Clamp(limit, PostingListPageSizeMinimum, PostingListPageSizeMaximum);
    }

    /// <summary>
    /// Formats an instant the way Ozon documents it: UTC with a <c>Z</c> designator. The offset form
    /// the serializer would otherwise emit is rejected by the API.
    /// </summary>
    public static string FormatUtc(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }
}
