using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// The <see cref="IOrdersWindowReader"/> for Ozon FBO: a <see cref="CursorPagedOrdersReader"/> bound
/// to <c>posting/fbo/list</c>. A separate concrete type per schema, because the shared reader takes
/// its method name at construction and two registrations of one type would collide in DI.
/// </summary>
public sealed class OzonFboOrdersReader : IOrdersWindowReader
{
    private readonly CursorPagedOrdersReader _inner;

    public OzonFboOrdersReader(
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
            OrdersRateLimiterMethods.OzonPostingFboList);
    }

    /// <inheritdoc/>
    public Task<FetchOutcome> ReadWindowAsync(
        OrdersRequest request,
        OrdersImportContext context,
        OrdersRunProgress progress,
        CancellationToken cancellationToken)
    {
        return _inner.ReadWindowAsync(WithSchema(request, OrdersSchema.Fbo), context, progress, cancellationToken);
    }

    // The reader owns the schema: a caller that forgets to set it must not silently be served the
    // endpoint's default schema, which would read FBO postings under the FBS job type.
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
