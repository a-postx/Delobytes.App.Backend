using Delobytes.App.Backend.Catalog.Application.Services;
using Delobytes.App.Backend.Sales.Contracts.Events;
using MassTransit;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Messaging.Consumers;

/// <summary>
/// Сonsumer for ProductOrdersCheckCompletedEvent, published by Sales after checking
/// Orders existence for a pending product deletion. Finalises or cancels the deletion through
/// <see cref="ProductDeletionService"/>.
/// </summary>
public class ProductOrdersCheckCompletedConsumer : IConsumer<ProductOrdersCheckCompletedEvent>
{
    private readonly ProductDeletionService _deletionService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductOrdersCheckCompletedConsumer"/> class.
    /// </summary>
    /// <param name="deletionService">Product deletion service.</param>
    public ProductOrdersCheckCompletedConsumer(ProductDeletionService deletionService)
    {
        _deletionService = deletionService;
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<ProductOrdersCheckCompletedEvent> context)
    {
        await _deletionService.CompleteOrdersCheckAsync(context.Message, context.CancellationToken);
    }
}
