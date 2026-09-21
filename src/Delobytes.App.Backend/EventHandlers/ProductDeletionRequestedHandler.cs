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

        int orderCount = await _salesContext.Orders
            .Where(o => notification.ChannelProductIds.Contains(o.ChannelProductId))
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
