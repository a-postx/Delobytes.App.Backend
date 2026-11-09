using Delobytes.App.Backend.Catalog.Contracts.Events;
using Delobytes.App.Backend.Sales.Application.Services;
using MassTransit;

namespace Delobytes.App.Backend.Sales.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consumer for <see cref="ChannelProductLinkedEvent"/>.
/// </summary>
public class ChannelProductLinkedConsumer : IConsumer<ChannelProductLinkedEvent>
{
    private readonly ChannelProductLinker _linker;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelProductLinkedConsumer"/> class.
    /// </summary>
    /// <param name="linker">Business logic consumer.</param>
    public ChannelProductLinkedConsumer(ChannelProductLinker linker)
    {
        _linker = linker;
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<ChannelProductLinkedEvent> context)
    {
        await _linker.UpdateChannelProductRefAsync(context.Message, context.CancellationToken);
    }
}
