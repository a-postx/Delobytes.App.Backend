using Delobytes.App.Backend.Constants;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Services;
using MassTransit;

namespace Delobytes.App.Backend.Filters;

/// <summary>
/// MassTransit publish filter that propagates the current correlation identifier into message headers.
/// The value comes from <see cref="ICorrelationContext"/>, which resolves it from the active HTTP
/// request or from the message currently being consumed, so a chain that starts at the API edge stays
/// intact across every hop. A new identifier is generated when none is present, such as messages
/// published on startup.
/// </summary>
/// <typeparam name="T">Message type.</typeparam>
public class CorrelationPublishFilter<T> : IFilter<PublishContext<T>>
    where T : class
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
        context.Headers.Set(
            CorrelationHeaders.CorrelationId,
            CorrelationIdProvider.Resolve(_correlationContext.CorrelationId));

        await next.Send(context);
    }

    /// <inheritdoc/>
    public void Probe(ProbeContext context)
    {
        context.CreateFilterScope("correlationPublish");
    }
}
