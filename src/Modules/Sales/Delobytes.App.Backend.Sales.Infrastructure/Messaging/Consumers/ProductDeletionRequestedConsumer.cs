using Delobytes.App.Backend.Catalog.Contracts.Events;
using Delobytes.App.Backend.Sales.Contracts.Events;
using Delobytes.App.Backend.Sales.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Sales.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consumer for ProductDeletionRequestedEvent, published by Catalog when a product with linked
/// ChannelProducts is requested for deletion. Checks whether Orders exist for the given
/// ChannelProduct IDs and reports the outcome back via ProductOrdersCheckCompletedEvent.
/// </summary>
public class ProductDeletionRequestedConsumer
{
    private readonly SalesDbContext _context;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<ProductDeletionRequestedConsumer> _logger;

    public ProductDeletionRequestedConsumer(
        SalesDbContext context,
        IPublishEndpoint publishEndpoint,
        ILogger<ProductDeletionRequestedConsumer> logger)
    {
        _context = context;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
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

        // The catalogue link lives on the line, not on the header, so the guard counts distinct
        // orders that have at least one line pointing at one of the requested channel products.
        int orderCount = await _context.OrderLines
            .Where(l => l.ChannelProductId != null
                && message.ChannelProductIds.Contains(l.ChannelProductId.Value))
            .Select(l => l.OrderId)
            .Distinct()
            .CountAsync(cancellationToken);

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
            CheckedAt = DateTimeOffset.UtcNow,
        };

        await _publishEndpoint.Publish(resultEvent, cancellationToken);
    }
}
