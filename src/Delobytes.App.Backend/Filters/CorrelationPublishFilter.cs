using Delobytes.App.Backend.Constants;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Services;
using MassTransit;

namespace Delobytes.App.Backend.Filters;

/// <summary>
/// MassTransit publish filter that propagates the current correlation identifier into message headers.
/// The value comes from <see cref="ICorrelationContext"/>, which resolves it from the active HTTP
/// request or from the message currently being consumed.
/// </summary>
/// <typeparam name="T">Message type.</typeparam>
public class CorrelationPublishFilter<T> : IFilter<PublishContext<T>> where T : class
{
    private readonly ICorrelationContext _correlationContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="CorrelationPublishFilter{T}"/> class.
    /// </summary>
    /// <param name="correlationContext">Correlation context.</param>
    public CorrelationPublishFilter(ICorrelationContext correlationContext)
    {
        _correlationContext = correlationContext;
    }

    /// <inheritdoc/>
    public async Task Send(PublishContext<T> context, IPipe<PublishContext<T>> next)
    {
        Guid correlationId = CorrelationIdProvider.Resolve(_correlationContext.CorrelationId);

        context.Headers.Set(CorrelationHeaders.CorrelationId, correlationId.ToString());

        // Only fill in the native field when it is still empty: a saga may already have set it
        // (via CorrelatedBy<Guid> or an explicit CorrelateById) to the business id it uses to find
        // its instance, and overwriting that with our chain id here would break saga lookup.
        if (!context.CorrelationId.HasValue || context.CorrelationId.Value == Guid.Empty)
        {
            context.CorrelationId = correlationId;
        }

        await next.Send(context);
    }

    /// <inheritdoc/>
    public void Probe(ProbeContext context)
    {
        context.CreateFilterScope("correlationPublish");
    }
}
