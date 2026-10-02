using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProduct;
using Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProducts;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog;

/// <summary>
/// A product linked to a marketplace card (ChannelProduct) must expose that link in both
/// GetProduct and GetProducts so the frontend can render a channel badge and disable editing
/// of fields the import will overwrite. A product without any link must come back with an
/// empty ChannelLinks list, so it stays fully editable.
/// </summary>
public class ProductChannelLinksTests
{
    private readonly Mock<IProductRepository> _repositoryMock = new();
    private readonly Mock<IProductPhotoService> _photoServiceMock = new();

    public ProductChannelLinksTests()
    {
        _photoServiceMock
            .Setup(s => s.GetPublicUrl(It.IsAny<ProductPhoto>()))
            .Returns((ProductPhoto photo) => $"https://storage.example/{photo.StorageKey}");
    }

    // ── GetProductQueryHandler ───────────────────────────────────────────

    [Fact]
    public async Task GetProduct_WithChannelProduct_ReturnsChannelLink()
    {
        // Arrange
        Guid channelId = Guid.NewGuid();
        Channel channel = BuildChannel(channelId, "Wildberries", "wildberries");
        Product product = BuildProduct();
        product.ChannelProducts.Add(new ChannelProduct
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ChannelId = channelId,
            Channel = channel,
            ExternalProductId = "123456789",
            ExternalSku = "SKU-001",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        _repositoryMock
            .Setup(r => r.GetByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        GetProductQueryHandler handler = new GetProductQueryHandler(_repositoryMock.Object, _photoServiceMock.Object);

        // Act
        GetProductResponse response = await handler.Handle(
            new GetProductQuery { Id = product.Id },
            CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        response.ChannelLinks.Should().ContainSingle();
        response.ChannelLinks![0].ChannelId.Should().Be(channelId);
        response.ChannelLinks[0].ChannelName.Should().Be("Wildberries");
        response.ChannelLinks[0].ChannelCode.Should().Be("wildberries");
        response.ChannelLinks[0].ExternalProductId.Should().Be("123456789");
        response.ChannelLinks[0].ExternalSku.Should().Be("SKU-001");
        response.ChannelLinks[0].IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetProduct_WithoutChannelProduct_ReturnsEmptyChannelLinks()
    {
        // Arrange: a manually created product has no marketplace link and stays editable.
        Product product = BuildProduct();

        _repositoryMock
            .Setup(r => r.GetByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        GetProductQueryHandler handler = new GetProductQueryHandler(_repositoryMock.Object, _photoServiceMock.Object);

        // Act
        GetProductResponse response = await handler.Handle(
            new GetProductQuery { Id = product.Id },
            CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        response.ChannelLinks.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProduct_WithInactiveChannelProduct_StillReturnsLinkButMarkedInactive()
    {
        // Arrange: a link stays visible even when IsActive = false (archived product, or
        // channel product deactivated on deletion) -- only its activity flag changes.
        Guid channelId = Guid.NewGuid();
        Product product = BuildProduct();
        product.ChannelProducts.Add(new ChannelProduct
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ChannelId = channelId,
            Channel = BuildChannel(channelId, "Ozon", "ozon"),
            ExternalProductId = "987654321",
            IsActive = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        _repositoryMock
            .Setup(r => r.GetByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        GetProductQueryHandler handler = new GetProductQueryHandler(_repositoryMock.Object, _photoServiceMock.Object);

        // Act
        GetProductResponse response = await handler.Handle(
            new GetProductQuery { Id = product.Id },
            CancellationToken.None);

        // Assert
        response.ChannelLinks.Should().ContainSingle();
        response.ChannelLinks![0].IsActive.Should().BeFalse();
    }

    // ── GetProductsQueryHandler ──────────────────────────────────────────

    [Fact]
    public async Task GetProducts_MapsChannelLinksPerProduct_WithoutMixingThemUp()
    {
        // Arrange
        Guid wbChannelId = Guid.NewGuid();
        Product linkedProduct = BuildProduct(name: "Linked product");
        linkedProduct.ChannelProducts.Add(new ChannelProduct
        {
            Id = Guid.NewGuid(),
            ProductId = linkedProduct.Id,
            ChannelId = wbChannelId,
            Channel = BuildChannel(wbChannelId, "Wildberries", "wildberries"),
            ExternalProductId = "111",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        Product manualProduct = BuildProduct(name: "Manual product");

        _repositoryMock
            .Setup(r => r.GetAllByStatusAsync(It.IsAny<ProductStatus?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Product> { linkedProduct, manualProduct });

        GetProductsQueryHandler handler = new GetProductsQueryHandler(_repositoryMock.Object, _photoServiceMock.Object);

        // Act
        GetProductsResponse response = await handler.Handle(new GetProductsQuery(), CancellationToken.None);

        // Assert
        response.Items.Should().HaveCount(2);

        ProductItem linkedItem = response.Items.Should().ContainSingle(i => i.Id == linkedProduct.Id).Which;
        linkedItem.ChannelLinks.Should().ContainSingle();
        linkedItem.ChannelLinks![0].ChannelCode.Should().Be("wildberries");
        linkedItem.ChannelLinks[0].ExternalProductId.Should().Be("111");

        ProductItem manualItem = response.Items.Should().ContainSingle(i => i.Id == manualProduct.Id).Which;
        manualItem.ChannelLinks.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProducts_ProductWithMultipleChannelLinks_ReturnsOneBadgePerLink()
    {
        // Arrange: the same product can be sold on several marketplaces at once.
        Guid wbChannelId = Guid.NewGuid();
        Guid ozonChannelId = Guid.NewGuid();
        Product product = BuildProduct();
        product.ChannelProducts.Add(new ChannelProduct
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ChannelId = wbChannelId,
            Channel = BuildChannel(wbChannelId, "Wildberries", "wildberries"),
            ExternalProductId = "111",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        product.ChannelProducts.Add(new ChannelProduct
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ChannelId = ozonChannelId,
            Channel = BuildChannel(ozonChannelId, "Ozon", "ozon"),
            ExternalProductId = "222",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        _repositoryMock
            .Setup(r => r.GetAllByStatusAsync(It.IsAny<ProductStatus?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Product> { product });

        GetProductsQueryHandler handler = new GetProductsQueryHandler(_repositoryMock.Object, _photoServiceMock.Object);

        // Act
        GetProductsResponse response = await handler.Handle(new GetProductsQuery(), CancellationToken.None);

        // Assert
        response.Items.Should().ContainSingle().Which.ChannelLinks.Should().HaveCount(2);
    }

    private static Product BuildProduct(string? name = null)
    {
        return new Product
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-" + Guid.NewGuid().ToString("N").Substring(0, 8),
            Name = name ?? "Test product",
            Status = ProductStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static Channel BuildChannel(Guid id, string name, string? code)
    {
        return new Channel
        {
            Id = id,
            Name = name,
            Code = code,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
