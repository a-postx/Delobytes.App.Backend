using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// Reads one orders window by following an opaque cursor forward, for channels that paginate a
/// window instead of serving it capped-and-bisectable the way Wildberries does (e.g. Ozon's
/// <c>posting/fbo/list</c>). Bound to one rate-limited API method and one in-process wait
/// tolerance through its tuning, so one instance paces exactly the method it was built for.
/// </summary>
public sealed class CursorPagedOrdersReader : IOrdersWindowReader
{
    private readonly PacedOrdersSource _pacedSource;
    private readonly ILogger<CursorPagedOrdersReader> _logger;
    private readonly OrdersSchema _schema;

    public CursorPagedOrdersReader(
        PacedOrdersSource pacedSource,
        ILogger<CursorPagedOrdersReader> logger,
        OrdersImportTuning tuning,
        OrdersSchema schema)
    {
        _pacedSource = pacedSource;
        _logger = logger;
        _schema = schema;
    }

    /// <summary>
    /// Follows <see cref="OrdersPage.NextCursor"/> from <see cref="OrdersRequest.Cursor"/> until the
    /// source reports <see cref="OrdersPage.HasMore"/> false, merging every page into one result. A
    /// throttle or budget stop on any page discards nothing already merged in memory, but returns it
    /// as a stop anyway: the caller must not publish a window the reader never finished.
    /// </summary>
    public async Task<FetchOutcome> ReadWindowAsync(
        OrdersRequest request,
        OrdersImportContext context,
        RunCounters counters,
        CancellationToken cancellationToken)
    {
        // The reader owns the schema: a caller that forgets to set it must not silently be served
        // the other endpoint's default schema.
        OrdersRequest pinnedRequest = WithSchema(request, _schema);

        Dictionary<string, ChannelOrderSnapshot> ordersByOrderKey = new Dictionary<string, ChannelOrderSnapshot>(StringComparer.Ordinal);
        string? cursor = pinnedRequest.Cursor;
        int rowsWithoutIdentity = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OrdersRequest pageRequest = ClonePageRequest(pinnedRequest, cursor);

            PageAttempt attempt = await _pacedSource.GetOrdersAsync(context, counters, pageRequest, cancellationToken);

            if (attempt is PageAttempt.Stop stop)
            {
                return new FetchOutcome.Stopped
                {
                    Cause = stop.Cause,
                    ResumeAt = stop.ResumeAt,
                    RequestsIssued = counters.RequestsIssued,
                };
            }

            OrdersPage page = ((PageAttempt.Page)attempt).Value;

            rowsWithoutIdentity += page.RowsWithoutIdentity;

            foreach (ChannelOrderSnapshot order in page.Orders)
            {
                if (!ordersByOrderKey.TryGetValue(order.ExternalOrderId, out ChannelOrderSnapshot? existing))
                {
                    ordersByOrderKey.Add(order.ExternalOrderId, order);
                    continue;
                }

                ordersByOrderKey[order.ExternalOrderId] = OrderSnapshotMerger.Merge(existing, order);
            }

            if (!page.HasMore)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(page.NextCursor) || string.Equals(page.NextCursor, cursor, StringComparison.Ordinal))
            {
                // HasMore with no new cursor would re-request the page just read forever; failing
                // fast here is the only safe reaction to a channel reporting more data without a
                // way to reach it.
                _logger.LogError(
                    "JobRun {JobRunId}: window {From} - {To} reported HasMore with a blank or unchanged cursor, failing the run",
                    context.JobRun.Id,
                    request.DateFrom,
                    request.DateTo);

                throw new InvalidOperationException(
                    $"Orders cursor pagination for window {request.DateFrom:O} - {request.DateTo:O} reported " +
                    "HasMore with a blank or unchanged cursor; the window cannot be paginated without looping forever.");
            }

            cursor = page.NextCursor;
        }

        if (rowsWithoutIdentity > 0)
        {
            _logger.LogError(
                "JobRun {JobRunId}: window {From} - {To} returned {RowsWithoutIdentity} row(s) without a usable order identity, failing the run",
                context.JobRun.Id,
                request.DateFrom,
                request.DateTo,
                rowsWithoutIdentity);

            throw new InvalidOperationException(
                $"Orders cursor pagination received {rowsWithoutIdentity} row(s) without a usable order identity " +
                $"in window {request.DateFrom:O} - {request.DateTo:O}; the window cannot be imported without " +
                "inventing an identity that may split or merge real orders.");
        }

        List<ChannelOrderSnapshot> orders = ordersByOrderKey.Values.ToList();

        return new FetchOutcome.DeltaRead
        {
            Orders = orders,
            RequestsIssued = counters.RequestsIssued,
        };
    }

    private static OrdersRequest WithSchema(OrdersRequest request, OrdersSchema schema)
    {
        if (request.Schema == schema)
        {
            return request;
        }

        return new OrdersRequest
        {
            DateFrom = request.DateFrom,
            DateTo = request.DateTo,
            Selection = request.Selection,
            Limit = request.Limit,
            Cursor = request.Cursor,
            Statuses = request.Statuses,
            Schema = schema,
        };
    }

    private static OrdersRequest ClonePageRequest(OrdersRequest source, string? cursor)
    {
        return new OrdersRequest
        {
            DateFrom = source.DateFrom,
            DateTo = source.DateTo,
            Selection = source.Selection,
            Limit = source.Limit,
            Cursor = cursor,
            Statuses = source.Statuses,
            Schema = source.Schema,
        };
    }
}
