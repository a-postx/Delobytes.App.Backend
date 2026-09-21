using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Commands.Products.ArchiveProduct;
using Delobytes.App.Backend.Catalog.Application.Commands.Products.RestoreProduct;
using Delobytes.App.Backend.Catalog.Application.Commands.Products.RequestProductDeletion;
using Delobytes.App.Backend.Catalog.Application.Events;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProductDeletionStatus;
using Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProducts;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.EventHandlers;
using Delobytes.App.Backend.Sales.Domain.Entities;
using Delobytes.App.Backend.Sales.Domain.Enums;
using Delobytes.App.Backend.Sales.Infrastructure.Persistence;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog;

/// <summary>
/// Lifecycle tests for Products: archiving, restore, and the two-phase deletion flow
/// (RequestProductDeletionCommand -> ProductDeletionRequested -> orders check in Sales ->
/// ProductOrdersCheckCompleted -> ProductOrdersCheckCompletedHandler -> Deleted or DeletionFailed).
///
/// The cross-module steps are exercised against real DbContexts (InMemory) because the
/// contract between the Catalog and Sales modules is expressed in EF queries, not in
/// repository method signatures -- mocks at that seam would prove nothing.
/// </summary>
public class ProductLifecycleTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    // ---------------------------------------------------------------- command handlers

    [Fact]
    public async Task ArchiveProduct_SetsStatusAndTimestamp()
    {
        // Arrange
        Product product = BuildProduct(ProductStatus.Active);
        Mock<IProductRepository> repositoryMock = BuildRepositoryMock(product);
        ArchiveProductCommandHandler handler = new ArchiveProductCommandHandler(repositoryMock.Object);

        // Act
        ArchiveProductResponse response = await handler.Handle(
            new ArchiveProductCommand { ProductId = product.Id },
            CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        product.Status.Should().Be(ProductStatus.Archived);
        product.ArchivedAt.Should().NotBeNull();
        product.ArchivedAt!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        product.ChannelProducts.Should().OnlyContain(cp => !cp.IsActive);

        repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ArchiveProduct_AlreadyDeleted_ReturnsNotFoundAndChangesNothing()
    {
        // Arrange
        Product product = BuildProduct(ProductStatus.Deleted);
        product.DeletedAt = DateTimeOffset.UtcNow;
        Mock<IProductRepository> repositoryMock = BuildRepositoryMock(product);
        ArchiveProductCommandHandler handler = new ArchiveProductCommandHandler(repositoryMock.Object);

        // Act
        ArchiveProductResponse response = await handler.Handle(
            new ArchiveProductCommand { ProductId = product.Id },
            CancellationToken.None);

        // Assert
        response.Found.Should().BeFalse();
        product.Status.Should().Be(ProductStatus.Deleted);
        product.ArchivedAt.Should().BeNull();

        repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RestoreProduct_FromArchived_SetsStatusToActive()
    {
        // Arrange
        Product product = BuildProduct(ProductStatus.Archived);
        product.ArchivedAt = DateTimeOffset.UtcNow.AddDays(-5);
        Mock<IProductRepository> repositoryMock = BuildRepositoryMock(product);
        RestoreProductCommandHandler handler = new RestoreProductCommandHandler(repositoryMock.Object);

        // Act
        RestoreProductResponse response = await handler.Handle(
            new RestoreProductCommand { ProductId = product.Id },
            CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        response.Accepted.Should().BeTrue();
        product.Status.Should().Be(ProductStatus.Active);
        product.ArchivedAt.Should().BeNull();
        product.ChannelProducts.Should().OnlyContain(cp => cp.IsActive);

        repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RestoreProduct_FromDeletionFailed_SetsStatusToActive()
    {
        // Arrange
        Product product = BuildProduct(ProductStatus.DeletionFailed);
        product.DeletionRequestedAt = DateTimeOffset.UtcNow.AddHours(-2);
        Mock<IProductRepository> repositoryMock = BuildRepositoryMock(product);
        RestoreProductCommandHandler handler = new RestoreProductCommandHandler(repositoryMock.Object);

        // Act
        RestoreProductResponse response = await handler.Handle(
            new RestoreProductCommand { ProductId = product.Id },
            CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        response.Accepted.Should().BeTrue();
        product.Status.Should().Be(ProductStatus.Active);
        product.DeletionRequestedAt.Should().BeNull();
        product.ArchivedAt.Should().BeNull();
    }

    [Fact]
    public async Task RestoreProduct_FromDeleted_IsRejected()
    {
        // Arrange: soft-deleted products are terminal -- no restore path is defined for them.
        Product product = BuildProduct(ProductStatus.Deleted);
        product.DeletedAt = DateTimeOffset.UtcNow;
        Mock<IProductRepository> repositoryMock = BuildRepositoryMock(product);
        RestoreProductCommandHandler handler = new RestoreProductCommandHandler(repositoryMock.Object);

        // Act
        RestoreProductResponse response = await handler.Handle(
            new RestoreProductCommand { ProductId = product.Id },
            CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        response.Accepted.Should().BeFalse();
        product.Status.Should().Be(ProductStatus.Deleted);
        product.DeletedAt.Should().NotBeNull();

        repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RequestProductDeletion_WithoutChannelProducts_DeletesImmediately()
    {
        // Arrange
        Product product = BuildProduct(ProductStatus.Active, channelProductCount: 0);
        Mock<IProductRepository> repositoryMock = BuildRepositoryMock(product);
        Mock<IPublisher> publisherMock = new Mock<IPublisher>();
        RequestProductDeletionCommandHandler handler = BuildDeletionRequestHandler(repositoryMock, publisherMock);

        // Act
        RequestProductDeletionResponse response = await handler.Handle(
            new RequestProductDeletionCommand { ProductId = product.Id },
            CancellationToken.None);

        // Assert: no cross-module check needed, so the product skips DeletionPending entirely.
        response.Found.Should().BeTrue();
        response.Accepted.Should().BeTrue();
        product.Status.Should().Be(ProductStatus.Deleted);
        product.DeletedAt.Should().NotBeNull();
        product.DeletionRequestedAt.Should().BeNull();

        publisherMock.Verify(
            p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RequestProductDeletion_WithChannelProducts_SetsPendingStatus()
    {
        // Arrange
        Product product = BuildProduct(ProductStatus.Active, channelProductCount: 2);
        List<Guid> expectedChannelProductIds = product.ChannelProducts.Select(cp => cp.Id).ToList();
        Mock<IProductRepository> repositoryMock = BuildRepositoryMock(product);
        Mock<IPublisher> publisherMock = new Mock<IPublisher>();
        RequestProductDeletionCommandHandler handler = BuildDeletionRequestHandler(repositoryMock, publisherMock);

        // Act
        RequestProductDeletionResponse response = await handler.Handle(
            new RequestProductDeletionCommand { ProductId = product.Id },
            CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        response.Accepted.Should().BeTrue();
        product.Status.Should().Be(ProductStatus.DeletionPending);
        product.DeletionRequestedAt.Should().NotBeNull();
        product.DeletedAt.Should().BeNull();

        publisherMock.Verify(
            p => p.Publish(
                It.Is<ProductDeletionRequested>(e =>
                    e.ProductId == product.Id &&
                    e.TenantId == _tenantId &&
                    e.ChannelProductIds.Count == 2 &&
                    expectedChannelProductIds.All(id => e.ChannelProductIds.Contains(id))),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RequestProductDeletion_AlreadyPending_IsRejectedWithoutRepublish()
    {
        // Arrange
        Product product = BuildProduct(ProductStatus.DeletionPending);
        product.DeletionRequestedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        Mock<IProductRepository> repositoryMock = BuildRepositoryMock(product);
        Mock<IPublisher> publisherMock = new Mock<IPublisher>();
        RequestProductDeletionCommandHandler handler = BuildDeletionRequestHandler(repositoryMock, publisherMock);

        // Act
        RequestProductDeletionResponse response = await handler.Handle(
            new RequestProductDeletionCommand { ProductId = product.Id },
            CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        response.Accepted.Should().BeFalse();
        response.ErrorMessage.Should().Be("Product deletion already in progress.");
        publisherMock.Verify(
            p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------------------------------------------- cross-module deletion flow (Sales)

    [Fact]
    public async Task ProductDeletionRequestedHandler_NoOrders_PublishesCheckCompleted()
    {
        // Arrange
        Guid productId = Guid.NewGuid();
        Mock<ITenantContext> tenantContextMock = BuildTenantContext();
        using SalesDbContext salesContext = BuildSalesContext(tenantContextMock);

        Mock<IPublisher> publisherMock = new Mock<IPublisher>();
        ProductDeletionRequestedHandler handler = new ProductDeletionRequestedHandler(
            salesContext,
            publisherMock.Object,
            Mock.Of<ILogger<ProductDeletionRequestedHandler>>());

        ProductDeletionRequested notification = new ProductDeletionRequested
        {
            ProductId = productId,
            ChannelProductIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() },
            TenantId = _tenantId,
            RequestedAt = DateTimeOffset.UtcNow,
        };

        // Act
        await handler.Handle(notification, CancellationToken.None);

        // Assert
        publisherMock.Verify(
            p => p.Publish(
                It.Is<ProductOrdersCheckCompleted>(e =>
                    e.ProductId == productId &&
                    e.HasOrders == false &&
                    e.OrderCount == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProductDeletionRequestedHandler_WithOrders_PublishesCheckCompletedWithCount()
    {
        // Arrange
        Guid requestedChannelProductId = Guid.NewGuid();
        Guid unrelatedChannelProductId = Guid.NewGuid();

        Mock<ITenantContext> tenantContextMock = BuildTenantContext();
        using SalesDbContext salesContext = BuildSalesContext(tenantContextMock);

        await SeedOrdersAsync(
            salesContext,
            BuildOrder(requestedChannelProductId),
            BuildOrder(requestedChannelProductId),
            BuildOrder(unrelatedChannelProductId));

        Mock<IPublisher> publisherMock = new Mock<IPublisher>();
        ProductDeletionRequestedHandler handler = new ProductDeletionRequestedHandler(
            salesContext,
            publisherMock.Object,
            Mock.Of<ILogger<ProductDeletionRequestedHandler>>());

        ProductDeletionRequested notification = new ProductDeletionRequested
        {
            ProductId = Guid.NewGuid(),
            ChannelProductIds = new List<Guid> { requestedChannelProductId },
            TenantId = _tenantId,
            RequestedAt = DateTimeOffset.UtcNow,
        };

        // Act
        await handler.Handle(notification, CancellationToken.None);

        // Assert: only orders of the requested channel products are counted.
        publisherMock.Verify(
            p => p.Publish(
                It.Is<ProductOrdersCheckCompleted>(e =>
                    e.HasOrders == true &&
                    e.OrderCount == 2),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProductDeletionFlow_NoOrders_EndsWithDeletedProduct()
    {
        // Arrange: full chain -- request deletion, check orders in Sales, complete deletion.
        Mock<ITenantContext> tenantContextMock = BuildTenantContext();
        using CatalogDbContext catalogContext = BuildCatalogContext(tenantContextMock);
        using SalesDbContext salesContext = BuildSalesContext(tenantContextMock);

        Product product = BuildProduct(ProductStatus.Active, channelProductCount: 3);
        catalogContext.Products.Add(product);
        await catalogContext.SaveChangesAsync();

        ProductRepository repository = new ProductRepository(catalogContext);

        Mock<IPublisher> publisherMock = new Mock<IPublisher>();
        ProductDeletionRequestedHandler salesHandler = new ProductDeletionRequestedHandler(
            salesContext,
            publisherMock.Object,
            Mock.Of<ILogger<ProductDeletionRequestedHandler>>());

        // Sales handler re-publishes into a no-op publisher: we drive the Catalog side explicitly.
        publisherMock
            .Setup(p => p.Publish(It.IsAny<ProductOrdersCheckCompleted>(), It.IsAny<CancellationToken>()))
            .Returns((ProductOrdersCheckCompleted e, CancellationToken ct) =>
                new ProductOrdersCheckCompletedHandler(
                    repository,
                    Mock.Of<ILogger<ProductOrdersCheckCompletedHandler>>()).Handle(e, ct));

        RequestProductDeletionCommandHandler requestHandler = new RequestProductDeletionCommandHandler(
            repository,
            publisherMock.Object,
            tenantContextMock.Object,
            Mock.Of<ILogger<RequestProductDeletionCommandHandler>>());

        // Act
        RequestProductDeletionResponse response = await requestHandler.Handle(
            new RequestProductDeletionCommand { ProductId = product.Id },
            CancellationToken.None);

        await salesHandler.Handle(
            new ProductDeletionRequested
            {
                ProductId = product.Id,
                ChannelProductIds = product.ChannelProducts.Select(cp => cp.Id).ToList(),
                TenantId = _tenantId,
                RequestedAt = DateTimeOffset.UtcNow,
            },
            CancellationToken.None);

        // Assert
        response.Accepted.Should().BeTrue();

        Product reloaded = await repository.GetWithChannelProductsByIdAsync(product.Id, CancellationToken.None);
        reloaded.Should().NotBeNull();
        reloaded!.Status.Should().Be(ProductStatus.Deleted);
        reloaded.DeletedAt.Should().NotBeNull();
        reloaded.DeletionRequestedAt.Should().NotBeNull("the timestamp is kept as an audit trail of the request");
        reloaded.ChannelProducts.Should().OnlyContain(cp => !cp.IsActive);
    }

    [Fact]
    public async Task ProductDeletionFlow_WithOrders_EndsWithDeletionFailedProduct()
    {
        // Arrange
        Mock<ITenantContext> tenantContextMock = BuildTenantContext();
        using CatalogDbContext catalogContext = BuildCatalogContext(tenantContextMock);
        using SalesDbContext salesContext = BuildSalesContext(tenantContextMock);

        Product product = BuildProduct(ProductStatus.Active, channelProductCount: 2);
        Guid orderedChannelProductId = product.ChannelProducts.First().Id;
        catalogContext.Products.Add(product);
        await catalogContext.SaveChangesAsync();

        await SeedOrdersAsync(salesContext, BuildOrder(orderedChannelProductId));

        ProductRepository repository = new ProductRepository(catalogContext);

        Mock<IPublisher> publisherMock = new Mock<IPublisher>();
        publisherMock
            .Setup(p => p.Publish(It.IsAny<ProductOrdersCheckCompleted>(), It.IsAny<CancellationToken>()))
            .Returns((ProductOrdersCheckCompleted e, CancellationToken ct) =>
                new ProductOrdersCheckCompletedHandler(
                    repository,
                    Mock.Of<ILogger<ProductOrdersCheckCompletedHandler>>()).Handle(e, ct));

        ProductDeletionRequestedHandler salesHandler = new ProductDeletionRequestedHandler(
            salesContext,
            publisherMock.Object,
            Mock.Of<ILogger<ProductDeletionRequestedHandler>>());

        RequestProductDeletionCommandHandler requestHandler = new RequestProductDeletionCommandHandler(
            repository,
            publisherMock.Object,
            tenantContextMock.Object,
            Mock.Of<ILogger<RequestProductDeletionCommandHandler>>());

        // Act
        await requestHandler.Handle(
            new RequestProductDeletionCommand { ProductId = product.Id },
            CancellationToken.None);

        await salesHandler.Handle(
            new ProductDeletionRequested
            {
                ProductId = product.Id,
                ChannelProductIds = product.ChannelProducts.Select(cp => cp.Id).ToList(),
                TenantId = _tenantId,
                RequestedAt = DateTimeOffset.UtcNow,
            },
            CancellationToken.None);

        // Assert
        Product reloaded = await repository.GetWithChannelProductsByIdAsync(product.Id, CancellationToken.None);
        reloaded.Should().NotBeNull();
        reloaded!.Status.Should().Be(ProductStatus.DeletionFailed);
        reloaded.DeletedAt.Should().BeNull();
        reloaded.DeletionRequestedAt.Should().BeNull();
        reloaded.ChannelProducts.Should().OnlyContain(cp => cp.IsActive, "the product remains sellable after a failed deletion");
    }

    // ---------------------------------------------- ProductOrdersCheckCompletedHandler

    [Fact]
    public async Task ProductOrdersCheckCompletedHandler_NoOrders_CompletesDelete()
    {
        // Arrange
        Product product = BuildProduct(ProductStatus.DeletionPending);
        product.DeletionRequestedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        Mock<IProductRepository> repositoryMock = BuildRepositoryMock(product);
        ProductOrdersCheckCompletedHandler handler = new ProductOrdersCheckCompletedHandler(
            repositoryMock.Object,
            Mock.Of<ILogger<ProductOrdersCheckCompletedHandler>>());

        ProductOrdersCheckCompleted notification = new ProductOrdersCheckCompleted
        {
            ProductId = product.Id,
            HasOrders = false,
            OrderCount = 0,
            CheckedAt = DateTimeOffset.UtcNow,
        };

        // Act
        await handler.Handle(notification, CancellationToken.None);

        // Assert
        product.Status.Should().Be(ProductStatus.Deleted);
        product.DeletedAt.Should().NotBeNull();
        product.ChannelProducts.Should().OnlyContain(cp => !cp.IsActive);
        repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProductOrdersCheckCompletedHandler_WithOrders_CancelsDelete()
    {
        // Arrange
        Product product = BuildProduct(ProductStatus.DeletionPending);
        product.DeletionRequestedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        Mock<IProductRepository> repositoryMock = BuildRepositoryMock(product);
        ProductOrdersCheckCompletedHandler handler = new ProductOrdersCheckCompletedHandler(
            repositoryMock.Object,
            Mock.Of<ILogger<ProductOrdersCheckCompletedHandler>>());

        ProductOrdersCheckCompleted notification = new ProductOrdersCheckCompleted
        {
            ProductId = product.Id,
            HasOrders = true,
            OrderCount = 5,
            CheckedAt = DateTimeOffset.UtcNow,
        };

        // Act
        await handler.Handle(notification, CancellationToken.None);

        // Assert
        product.Status.Should().Be(ProductStatus.DeletionFailed);
        product.DeletionRequestedAt.Should().BeNull();
        product.DeletedAt.Should().BeNull();
        repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProductOrdersCheckCompletedHandler_ProductNotPending_IsIgnored()
    {
        // Arrange: a late or duplicated check result must not resurrect a deleted product.
        Product product = BuildProduct(ProductStatus.Active);
        Mock<IProductRepository> repositoryMock = BuildRepositoryMock(product);
        ProductOrdersCheckCompletedHandler handler = new ProductOrdersCheckCompletedHandler(
            repositoryMock.Object,
            Mock.Of<ILogger<ProductOrdersCheckCompletedHandler>>());

        ProductOrdersCheckCompleted notification = new ProductOrdersCheckCompleted
        {
            ProductId = product.Id,
            HasOrders = false,
            OrderCount = 0,
            CheckedAt = DateTimeOffset.UtcNow,
        };

        // Act
        await handler.Handle(notification, CancellationToken.None);

        // Assert
        product.Status.Should().Be(ProductStatus.Active);
        product.DeletedAt.Should().BeNull();
        repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProductOrdersCheckCompletedHandler_UnknownProduct_DoesNotThrow()
    {
        // Arrange
        Mock<IProductRepository> repositoryMock = new Mock<IProductRepository>();
        repositoryMock
            .Setup(r => r.GetWithChannelProductsByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        ProductOrdersCheckCompletedHandler handler = new ProductOrdersCheckCompletedHandler(
            repositoryMock.Object,
            Mock.Of<ILogger<ProductOrdersCheckCompletedHandler>>());

        // Act
        Func<Task> act = () => handler.Handle(
            new ProductOrdersCheckCompleted
            {
                ProductId = Guid.NewGuid(),
                HasOrders = false,
                OrderCount = 0,
                CheckedAt = DateTimeOffset.UtcNow,
            },
            CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------- queries / visibility

    [Fact]
    public async Task GetProducts_DefaultFilter_ReturnsOnlyActive()
    {
        // Arrange
        Mock<ITenantContext> tenantContextMock = BuildTenantContext();
        using CatalogDbContext catalogContext = BuildCatalogContext(tenantContextMock);

        catalogContext.Products.AddRange(
            BuildProduct(ProductStatus.Active, name: "Active A"),
            BuildProduct(ProductStatus.Active, name: "Active B"),
            BuildProduct(ProductStatus.Archived, name: "Archived"),
            BuildProduct(ProductStatus.DeletionPending, name: "Pending"),
            BuildProduct(ProductStatus.Deleted, name: "Deleted"));

        await catalogContext.SaveChangesAsync();

        ProductRepository repository = new ProductRepository(catalogContext);
        GetProductsQueryHandler handler = new GetProductsQueryHandler(repository);

        // Act
        GetProductsResponse response = await handler.Handle(new GetProductsQuery(), CancellationToken.None);

        // Assert
        response.Items.Should().HaveCount(2)
            .And.OnlyContain(i => i.Status == ProductStatus.Active)
            .And.OnlyContain(i => i.Name == "Active A" || i.Name == "Active B");
    }

    [Fact]
    public async Task GetProducts_ArchivedFilter_ReturnsOnlyArchived()
    {
        // Arrange
        Mock<ITenantContext> tenantContextMock = BuildTenantContext();
        using CatalogDbContext catalogContext = BuildCatalogContext(tenantContextMock);

        catalogContext.Products.AddRange(
            BuildProduct(ProductStatus.Active, name: "Active A"),
            BuildProduct(ProductStatus.Archived, name: "Archived A"),
            BuildProduct(ProductStatus.Archived, name: "Archived B"),
            BuildProduct(ProductStatus.Deleted, name: "Deleted"));

        await catalogContext.SaveChangesAsync();

        GetProductsQueryHandler handler = new GetProductsQueryHandler(new ProductRepository(catalogContext));

        // Act
        GetProductsResponse response = await handler.Handle(
            new GetProductsQuery { Status = ProductStatus.Archived },
            CancellationToken.None);

        // Assert
        response.Items.Should().HaveCount(2).And.OnlyContain(i => i.Status == ProductStatus.Archived);
    }

    [Fact]
    public async Task GetProducts_OrderedByName()
    {
        // Arrange: the repository promises a stable name ordering, the UI relies on it.
        Mock<ITenantContext> tenantContextMock = BuildTenantContext();
        using CatalogDbContext catalogContext = BuildCatalogContext(tenantContextMock);

        catalogContext.Products.AddRange(
            BuildProduct(ProductStatus.Active, name: "Charlie"),
            BuildProduct(ProductStatus.Active, name: "Alpha"),
            BuildProduct(ProductStatus.Active, name: "Bravo"));

        await catalogContext.SaveChangesAsync();

        GetProductsQueryHandler handler = new GetProductsQueryHandler(new ProductRepository(catalogContext));

        // Act
        GetProductsResponse response = await handler.Handle(new GetProductsQuery(), CancellationToken.None);

        // Assert
        response.Items.Select(i => i.Name).Should().ContainInOrder("Alpha", "Bravo", "Charlie");
    }

    [Fact]
    public async Task GetProductDeletionStatus_PendingProduct_ReturnsCorrectMessage()
    {
        // Arrange
        Product product = BuildProduct(ProductStatus.DeletionPending);
        product.DeletionRequestedAt = DateTimeOffset.UtcNow.AddMinutes(-1);

        Mock<IProductRepository> repositoryMock = new Mock<IProductRepository>();
        repositoryMock
            .Setup(r => r.GetByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        GetProductDeletionStatusQueryHandler handler = new GetProductDeletionStatusQueryHandler(repositoryMock.Object);

        // Act
        GetProductDeletionStatusResponse response = await handler.Handle(
            new GetProductDeletionStatusQuery { ProductId = product.Id },
            CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        response.Status.Should().Be(ProductStatus.DeletionPending);
        response.StatusMessage.Should().Be("Deletion in progress, checking for orders...");
        response.DeletionRequestedAt.Should().Be(product.DeletionRequestedAt);
        response.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task GetProductDeletionStatus_DeletionFailedProduct_ExplainsOrdersBlockedDeletion()
    {
        // Arrange
        Product product = BuildProduct(ProductStatus.DeletionFailed);

        Mock<IProductRepository> repositoryMock = new Mock<IProductRepository>();
        repositoryMock
            .Setup(r => r.GetByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        GetProductDeletionStatusQueryHandler handler = new GetProductDeletionStatusQueryHandler(repositoryMock.Object);

        // Act
        GetProductDeletionStatusResponse response = await handler.Handle(
            new GetProductDeletionStatusQuery { ProductId = product.Id },
            CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        response.Status.Should().Be(ProductStatus.DeletionFailed);
        response.StatusMessage.Should().Be("Deletion failed: product has existing orders.");
    }

    [Fact]
    public async Task GetProductDeletionStatus_DeletedProduct_ReportsSuccess()
    {
        // Arrange
        Product product = BuildProduct(ProductStatus.Deleted);
        product.DeletedAt = DateTimeOffset.UtcNow;

        Mock<IProductRepository> repositoryMock = new Mock<IProductRepository>();
        repositoryMock
            .Setup(r => r.GetByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        GetProductDeletionStatusQueryHandler handler = new GetProductDeletionStatusQueryHandler(repositoryMock.Object);

        // Act
        GetProductDeletionStatusResponse response = await handler.Handle(
            new GetProductDeletionStatusQuery { ProductId = product.Id },
            CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        response.StatusMessage.Should().Be("Product deleted successfully.");
        response.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetProductDeletionStatus_UnknownProduct_ReturnsNotFound()
    {
        // Arrange
        Mock<IProductRepository> repositoryMock = new Mock<IProductRepository>();
        repositoryMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        GetProductDeletionStatusQueryHandler handler = new GetProductDeletionStatusQueryHandler(repositoryMock.Object);

        // Act
        GetProductDeletionStatusResponse response = await handler.Handle(
            new GetProductDeletionStatusQuery { ProductId = Guid.NewGuid() },
            CancellationToken.None);

        // Assert
        response.Found.Should().BeFalse();
        response.StatusMessage.Should().BeEmpty();
    }

    // ------------------------------------------------------------------------- builders

    private static Product BuildProduct(
        ProductStatus status,
        string? name = null,
        int channelProductCount = 2)
    {
        Product product = new Product
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-" + Guid.NewGuid().ToString("N").Substring(0, 8),
            Name = name ?? "Test product",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
        };

        for (int i = 0; i < channelProductCount; i++)
        {
            product.ChannelProducts.Add(new ChannelProduct
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                ChannelId = Guid.NewGuid(),
                ExternalProductId = "EXT-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                IsActive = status == ProductStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
            });
        }

        return product;
    }

    private static Mock<IProductRepository> BuildRepositoryMock(Product product)
    {
        Mock<IProductRepository> repositoryMock = new Mock<IProductRepository>();

        repositoryMock
            .Setup(r => r.GetWithChannelProductsByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        repositoryMock
            .Setup(r => r.GetByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        return repositoryMock;
    }

    private RequestProductDeletionCommandHandler BuildDeletionRequestHandler(
        Mock<IProductRepository> repositoryMock,
        Mock<IPublisher> publisherMock)
    {
        return new RequestProductDeletionCommandHandler(
            repositoryMock.Object,
            publisherMock.Object,
            BuildTenantContext().Object,
            Mock.Of<ILogger<RequestProductDeletionCommandHandler>>());
    }

    private Mock<ITenantContext> BuildTenantContext()
    {
        Mock<ITenantContext> tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.TenantId).Returns(_tenantId);
        return tenantContextMock;
    }

    private static CatalogDbContext BuildCatalogContext(Mock<ITenantContext> tenantContextMock)
    {
        DbContextOptions<CatalogDbContext> options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("catalog-product-lifecycle-" + Guid.NewGuid())
            .Options;

        return new CatalogDbContext(options, tenantContextMock.Object);
    }

    private static SalesDbContext BuildSalesContext(Mock<ITenantContext> tenantContextMock)
    {
        DbContextOptions<SalesDbContext> options = new DbContextOptionsBuilder<SalesDbContext>()
            .UseInMemoryDatabase("sales-product-lifecycle-" + Guid.NewGuid())
            .Options;

        return new SalesDbContext(options, tenantContextMock.Object);
    }

    private static Order BuildOrder(Guid channelProductId)
    {
        return new Order
        {
            Id = Guid.NewGuid(),
            ChannelProductId = channelProductId,
            ExternalOrderId = "ORD-" + Guid.NewGuid().ToString("N").Substring(0, 8),
            ChannelId = Guid.NewGuid(),
            OrderDate = DateTimeOffset.UtcNow.AddDays(-1),
            Quantity = 1,
            Revenue = 1000m,
            Commission = 150m,
            NetRevenue = 850m,
            Status = OrderStatus.Confirmed,
            ImportedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static async Task SeedOrdersAsync(SalesDbContext salesContext, params Order[] orders)
    {
        salesContext.Orders.AddRange(orders);
        await salesContext.SaveChangesAsync();
    }
}
