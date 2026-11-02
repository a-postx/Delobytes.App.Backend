using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// The <see cref="IOrdersWindowReader"/> for Ozon FBS: a <see cref="CursorPagedOrdersReader"/> bound
/// to <c>posting/fbs/list</c>. Separate from <see cref="OzonFboOrdersReader"/> because the pager is
/// paced per API method, so the two schemas must not share one instance.
/// </summary>
public sealed class OzonFbsOrdersReader : IOrdersWindowReader
{
    private readonly CursorPagedOrdersReader _inner;

    public OzonFbsOrdersReader(
        PacedOrdersSource pacedSource,
        ILogger<CursorPagedOrdersReader> logger,
        IOptions<OzonImportOptions> options)
    {
        OzonImportOptions importOptions = options.Value;

        _inner = new CursorPagedOrdersReader(
            pacedSource,
            logger,
            TimeSpan.FromSeconds(importOptions.MaxInProcessWaitSeconds >= 0
                ? importOptions.MaxInProcessWaitSeconds
                : OzonImportOptions.DefaultMaxInProcessWaitSeconds),
            OrdersRateLimiterMethods.OzonPostingFbsList);
    }

    /// <inheritdoc/>
    public Task<FetchOutcome> ReadWindowAsync(
        OrdersRequest request,
        OrdersImportContext context,
        OrdersRunProgress progress,
        CancellationToken cancellationToken)
    {
        return _inner.ReadWindowAsync(WithSchema(request, OrdersSchema.Fbs), context, progress, cancellationToken);
    }

    // Pinning the schema here keeps a caller that forgets to set it from being served the FBO
    // endpoint's default schema while running under the FBS job type.
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
}
