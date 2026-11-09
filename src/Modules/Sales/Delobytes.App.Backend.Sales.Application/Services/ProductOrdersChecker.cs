using Delobytes.App.Backend.Catalog.Contracts.Events;
using Delobytes.App.Backend.Sales.Application.Interfaces;
using Delobytes.App.Backend.Sales.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Sales.Contracts.Events;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Sales.Application.Services;

/// <summary>
/// Handles ProductDeletionRequestedEvent, published by Catalog when a product with linked
/// ChannelProducts is requested for deletion. Checks whether Orders exist for the given
/// ChannelProduct IDs and reports the outcome back via ProductOrdersCheckCompletedEvent.
/// </summary>
public class ProductOrdersChecker
{
    private readonly IProductOrdersRepository _store;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<ProductOrdersChecker> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductOrdersChecker"/> class.
    /// </summary>
    /// <param name="store">Read-side port that counts orders per channel product.</param>
    /// <param name="eventPublisher">Event publisher.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="timeProvider">Clock source.</param>
    public ProductOrdersChecker(
        IProductOrdersRepository store,
        IEventPublisher eventPublisher,
        ILogger<ProductOrdersChecker> logger,
        TimeProvider timeProvider)
    {
        _store = store;
        _eventPublisher = eventPublisher;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Processes the ProductDeletionRequestedEvent message.
    /// </summary>
    /// <param name="message">Event message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ProcessAsync(ProductDeletionRequestedEvent message, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Checking orders for Product {ProductId} across {Count} channel product(s)",
            message.ProductId,
            message.ChannelProductIds.Count);

        int orderCount = await _store.CountOrdersForChannelProductsAsync(
            message.ChannelProductIds, cancellationToken);

        bool hasOrders = orderCount > 0;

        _logger.LogInformation(
            "Product {ProductId}: {OrderCount} order(s) found, HasOrders={HasOrders}",
            message.ProductId,
            orderCount,
            hasOrders);

        ProductOrdersCheckCompletedEvent resultEvent = new ProductOrdersCheckCompletedEvent
        {
            ProductId = message.ProductId,
            HasOrders = hasOrders,
            OrderCount = orderCount,
            CheckedAt = _timeProvider.GetUtcNow(),
        };

        await _eventPublisher.PublishAsync(resultEvent, cancellationToken);
    }
}
