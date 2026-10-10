using Delobytes.App.Backend.Catalog.Application.Events;
using Delobytes.App.Backend.Sales.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.EventHandlers;

/// <summary>
/// Handles ProductDeletionRequested from the Catalog module.
/// Checks whether Orders exist for the given ChannelProductIds using the Sales DbContext.
/// Lives in the host project because it is the only place with access to both modules' DbContexts.
/// </summary>
public class ProductDeletionRequestedHandler : INotificationHandler<ProductDeletionRequested>
{
    private readonly SalesDbContext _salesContext;
    private readonly IPublisher _publisher;
    private readonly ILogger<ProductDeletionRequestedHandler> _logger;

    public ProductDeletionRequestedHandler(
        SalesDbContext salesContext,
        IPublisher publisher,
        ILogger<ProductDeletionRequestedHandler> logger)
    {
        _salesContext = salesContext;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task Handle(ProductDeletionRequested notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Checking orders for Product {ProductId} across {Count} channel product(s)",
            notification.ProductId,
            notification.ChannelProductIds.Count);

        // The catalogue link lives on the line, not on the header, so the guard counts distinct
        // orders that have at least one line pointing at one of the requested channel products.
        int orderCount = await _salesContext.OrderLines
            .Where(l => l.ChannelProductId != null
                && notification.ChannelProductIds.Contains(l.ChannelProductId.Value))
            .Select(l => l.OrderId)
            .Distinct()
            .CountAsync(cancellationToken);

        bool hasOrders = orderCount > 0;

        _logger.LogInformation(
            "Product {ProductId}: {OrderCount} order(s) found, HasOrders={HasOrders}",
            notification.ProductId,
            orderCount,
            hasOrders);

        ProductOrdersCheckCompleted resultEvent = new ProductOrdersCheckCompleted
        {
            ProductId = notification.ProductId,
            HasOrders = hasOrders,
            OrderCount = orderCount,
            CheckedAt = DateTimeOffset.UtcNow,
        };

        await _publisher.Publish(resultEvent, cancellationToken);
    }
}
