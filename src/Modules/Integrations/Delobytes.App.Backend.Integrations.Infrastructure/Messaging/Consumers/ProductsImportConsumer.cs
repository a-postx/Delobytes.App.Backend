using Delobytes.App.Backend.Integrations.Application.Events;
using Delobytes.App.Backend.Integrations.Application.Products;
using MassTransit;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// MassTransit adapter for <see cref="ProductsImportOrchestrator"/>.
/// </summary>
public class ProductsImportConsumer : IConsumer<ProductsImportRequestedEvent>
{
    private readonly ProductsImportOrchestrator _orchestrator;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductsImportConsumer"/> class.
    /// </summary>
    /// <param name="orchestrator">Products import orchestrator.</param>
    public ProductsImportConsumer(ProductsImportOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<ProductsImportRequestedEvent> context)
    {
        await _orchestrator.ProcessAsync(context.Message, context.CancellationToken);
    }
}
