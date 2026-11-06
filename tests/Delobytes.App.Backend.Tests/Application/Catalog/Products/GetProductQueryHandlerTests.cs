using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.Products;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog.Products;

/// <summary>
/// Tests the photo mapping of <see cref="GetProductQueryHandler"/>: every <c>Uploaded</c> row of
/// every channel is returned with its <c>ChannelId</c>, all size variants included, and nothing
/// else is exposed.
/// </summary>
public class GetProductQueryHandlerTests
{
    private readonly Mock<IProductRepository> _repositoryMock;
    private readonly Mock<IProductPhotoService> _photoServiceMock;
    private readonly GetProductQueryHandler _handler;

    public GetProductQueryHandlerTests()
    {
        _repositoryMock = new Mock<IProductRepository>();
        _photoServiceMock = new Mock<IProductPhotoService>();
        _handler = new GetProductQueryHandler(_repositoryMock.Object, _photoServiceMock.Object);

        _photoServiceMock
            .Setup(s => s.GetPublicUrl(It.IsAny<ProductPhoto>()))
            .Returns<ProductPhoto>(p => "https://cdn.test/" + p.StorageKey);
    }

    [Fact]
    public async Task Handle_ReturnsPhotosOfAllChannelsWithChannelId()
    {
        Guid firstChannelId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        Guid secondChannelId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        Product product = BuildProduct();
        product.Photos.Add(BuildPhoto(firstChannelId, 1, "thumbnail", ProductPhotoStatus.Uploaded));
        product.Photos.Add(BuildPhoto(firstChannelId, 1, "large", ProductPhotoStatus.Uploaded));
        product.Photos.Add(BuildPhoto(secondChannelId, 1, "thumbnail", ProductPhotoStatus.Uploaded));
        product.Photos.Add(BuildPhoto(Guid.Empty, 2, "thumbnail", ProductPhotoStatus.Uploaded));

        _repositoryMock
            .Setup(r => r.GetByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        GetProductResponse response = await _handler.Handle(
            new GetProductQuery { Id = product.Id },
            CancellationToken.None);

        response.Found.Should().BeTrue();
        response.Photos.Should().HaveCount(4);
        response.Photos!.Select(p => p.ChannelId).Should().BeEquivalentTo(
            new[] { firstChannelId, firstChannelId, secondChannelId, Guid.Empty });
        response.Photos!.Select(p => p.SizeVariant).Should().BeEquivalentTo(
            new[] { "thumbnail", "large", "thumbnail", "thumbnail" });
        response.Photos!.Should().OnlyContain(p => p.Url.StartsWith("https://cdn.test/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handle_ExcludesRemovedFailedAndPendingPhotos()
    {
        Guid channelId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        Product product = BuildProduct();
        product.Photos.Add(BuildPhoto(channelId, 1, "thumbnail", ProductPhotoStatus.Uploaded));
        product.Photos.Add(BuildPhoto(channelId, 2, "thumbnail", ProductPhotoStatus.Removed));
        product.Photos.Add(BuildPhoto(channelId, 3, "thumbnail", ProductPhotoStatus.Failed));
        product.Photos.Add(BuildPhoto(channelId, 4, "thumbnail", ProductPhotoStatus.Pending));

        _repositoryMock
            .Setup(r => r.GetByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        GetProductResponse response = await _handler.Handle(
            new GetProductQuery { Id = product.Id },
            CancellationToken.None);

        ProductPhotoDto photo = response.Photos.Should().ContainSingle().Subject;

        photo.DisplayOrder.Should().Be(1);
        photo.ChannelId.Should().Be(channelId);
    }

    [Fact]
    public async Task Handle_WhenProductMissing_ReturnsNotFoundWithoutPhotos()
    {
        _repositoryMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        GetProductResponse response = await _handler.Handle(
            new GetProductQuery { Id = Guid.NewGuid() },
            CancellationToken.None);

        response.Found.Should().BeFalse();
        response.Photos.Should().BeNull();
    }

    private static ProductPhoto BuildPhoto(
        Guid channelId,
        int displayOrder,
        string sizeVariant,
        ProductPhotoStatus status)
    {
        return new ProductPhoto
        {
            Id = Guid.NewGuid(),
            ChannelId = channelId,
            DisplayOrder = displayOrder,
            SizeVariant = sizeVariant,
            StorageKey = "product-photos/" + Guid.NewGuid().ToString("N") + ".webp",
            OriginalFileName = "photo.webp",
            ContentType = "image/webp",
            SizeBytes = 128,
            Source = "Wildberries",
            ExternalId = $"1_{displayOrder}_{sizeVariant}",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static Product BuildProduct()
    {
        return new Product
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-" + Guid.NewGuid().ToString("N").Substring(0, 8),
            Name = "Test product",
            Status = ProductStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
