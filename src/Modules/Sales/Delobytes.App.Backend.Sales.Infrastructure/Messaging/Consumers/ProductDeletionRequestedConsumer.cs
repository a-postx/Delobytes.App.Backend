using Delobytes.App.Backend.Catalog.Contracts.Events;
using Delobytes.App.Backend.Sales.Application.Services;
using MassTransit;

namespace Delobytes.App.Backend.Sales.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consumer for <see cref="ProductDeletionRequestedEvent"/>.
/// </summary>
public class ProductDeletionRequestedConsumer : IConsumer<ProductDeletionRequestedEvent>
{
    private readonly ProductOrdersChecker _checker;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductDeletionRequestedConsumer"/> class.
    /// </summary>
    /// <param name="checker">Business logic consumer.</param>
    public ProductDeletionRequestedConsumer(ProductOrdersChecker checker)
    {
        _checker = checker;
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<ProductDeletionRequestedEvent> context)
    {
        await _checker.ProcessAsync(context.Message, context.CancellationToken);
    }
}
