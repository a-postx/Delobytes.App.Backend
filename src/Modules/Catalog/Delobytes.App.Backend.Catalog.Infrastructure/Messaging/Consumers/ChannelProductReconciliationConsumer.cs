using Delobytes.App.Backend.Catalog.Application.Services;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using MassTransit;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Messaging.Consumers;

/// <summary>
/// MassTransit adapter for <see cref="ChannelProductReconciliationService"/>.
/// </summary>
public class ChannelProductReconciliationConsumer
    : IConsumer<ChannelProductReconciliationRequestedEvent>
{
    private readonly ChannelProductReconciliationService _service;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="ChannelProductReconciliationConsumer"/> class.
    /// </summary>
    /// <param name="service">Application-service that reconciles the full set.</param>
    public ChannelProductReconciliationConsumer(
        ChannelProductReconciliationService service)
    {
        _service = service;
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<ChannelProductReconciliationRequestedEvent> context)
    {
        await _service.ReconciliateAsync(context.Message, context.CancellationToken);
    }
}
