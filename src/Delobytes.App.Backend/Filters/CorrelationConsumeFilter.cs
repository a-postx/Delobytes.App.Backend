using Delobytes.App.Backend.Constants;
using Delobytes.App.Backend.Services;
using MassTransit;

namespace Delobytes.App.Backend.Filters;

/// <summary>
/// MassTransit consume filter that restores the correlation identifier from message headers.
/// The value is made available through <see cref="CorrelationContext"/> for the duration of message
/// processing, so everything logged by a consumer, and everything the consumer publishes further
/// down the chain, carries the same identifier as the HTTP request that started it.
/// </summary>
/// <typeparam name="T">Message type.</typeparam>
public class CorrelationConsumeFilter<T> : IFilter<ConsumeContext<T>> where T : class
{
    private readonly CorrelationContext _correlationContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="CorrelationConsumeFilter{T}"/> class.
    /// </summary>
    /// <param name="correlationContext">Correlation context.</param>
    public CorrelationConsumeFilter(CorrelationContext correlationContext)
    {
        _correlationContext = correlationContext;
    }

    /// <inheritdoc/>
    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        string? header = context.Headers.Get<string>(CorrelationHeaders.CorrelationId);

        _correlationContext.SetCorrelationId(CorrelationIdProvider.Resolve(header));

        try
        {
            await next.Send(context);
        }
        finally
        {
            // Always clear after message processing so the identifier cannot leak into the next
            // message handled by the same consumer scope.
            _correlationContext.SetCorrelationId(null);
        }
    }

    /// <inheritdoc/>
    public void Probe(ProbeContext context)
    {
        context.CreateFilterScope("correlationConsume");
    }
}
