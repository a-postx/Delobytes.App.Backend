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
/// Tests the photo and channel mapping of <see cref="GetProductQueryHandler"/>: the product's own
/// gallery returns every <c>Uploaded</c> canonical row, channel galleries and barcodes travel on
/// the link, and nothing else is exposed.
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
            .Setup(s => s.GetPublicUrl(It.IsAny<string>()))
            .Returns<string>(storageKey => "https://cdn.test/" + storageKey);
    }

    [Fact]
    public async Task Handle_ReturnsEveryUploadedCanonicalPhoto()
    {
        Product product = BuildProduct();
        product.Photos.Add(BuildPhoto(1, "thumbnail", ProductPhotoStatus.Uploaded));
        product.Photos.Add(BuildPhoto(1, "large", ProductPhotoStatus.Uploaded));
        product.Photos.Add(BuildPhoto(2, "thumbnail", ProductPhotoStatus.Uploaded));

        _repositoryMock
            .Setup(r => r.GetByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        GetProductResponse response = await _handler.Handle(
            new GetProductQuery { Id = product.Id },
            CancellationToken.None);

        response.Found.Should().BeTrue();
        response.Photos.Should().HaveCount(3);
        response.Photos!.Select(p => p.DisplayOrder).Should().Equal(1, 1, 2);
        response.Photos!.Select(p => p.SizeVariant).Should().BeEquivalentTo(
            new[] { "thumbnail", "large", "thumbnail" });
        response.Photos!.Should().OnlyContain(p => p.Url.StartsWith("https://cdn.test/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handle_ExcludesRemovedFailedAndPendingPhotos()
    {
        Product product = BuildProduct();
        product.Photos.Add(BuildPhoto(1, "thumbnail", ProductPhotoStatus.Uploaded));
        product.Photos.Add(BuildPhoto(2, "thumbnail", ProductPhotoStatus.Removed));
        product.Photos.Add(BuildPhoto(3, "thumbnail", ProductPhotoStatus.Failed));
        product.Photos.Add(BuildPhoto(4, "thumbnail", ProductPhotoStatus.Pending));

        _repositoryMock
            .Setup(r => r.GetByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        GetProductResponse response = await _handler.Handle(
            new GetProductQuery { Id = product.Id },
            CancellationToken.None);

        ProductPhotoDto photo = response.Photos.Should().ContainSingle().Subject;

        photo.DisplayOrder.Should().Be(1);
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
        int displayOrder,
        string sizeVariant,
        ProductPhotoStatus status)
    {
        return new ProductPhoto
        {
            Id = Guid.NewGuid(),
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

    [Fact]
    public async Task Handle_ChannelLinkCarriesChannelFieldsAndOwnUnit_AndCanonicalIsTheRowWithoutChannel()
    {
        Guid channelId = Guid.NewGuid();
        Product product = BuildProduct();
        product.Code = "P-000007";

        product.PackingUnits.Add(new PackingUnit
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ChannelId = channelId,
            LengthCm = 40m,
            WidthCm = 50m,
            HeightCm = 60m,
            WeightKg = 2m,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        product.PackingUnits.Add(new PackingUnit
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ChannelId = null,
            LengthCm = 10m,
            WidthCm = 20m,
            HeightCm = 30m,
            WeightKg = 1m,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        product.ChannelProducts.Add(new ChannelProduct
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ChannelId = channelId,
            ExternalProductId = "1088660899",
            ExternalSku = "квр-10",
            Name = "Channel name",
            Description = "Channel description",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            Channel = new Channel
            {
                Id = channelId,
                Name = "Ozon",
                Code = "ozon",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            },
        });

        _repositoryMock
            .Setup(r => r.GetByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        GetProductResponse response = await _handler.Handle(
            new GetProductQuery { Id = product.Id },
            CancellationToken.None);

        response.Code.Should().Be("P-000007");
        response.PackingUnit!.LengthCm.Should().Be(10m, "the canonical unit is the row without a channel");

        ProductChannelLinkDto link = response.ChannelLinks.Should().ContainSingle().Subject;
        link.Name.Should().Be("Channel name");
        link.Description.Should().Be("Channel description");
        link.PackingUnit!.LengthCm.Should().Be(40m, "the link carries the channel's own unit");
    }

    [Fact]
    public async Task Handle_OnlyAChannelUnit_ReturnsNullCanonicalAndStillCarriesTheChannelRow()
    {
        Guid channelId = Guid.NewGuid();
        Product product = BuildProduct();

        product.PackingUnits.Add(new PackingUnit
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ChannelId = channelId,
            LengthCm = 40m,
            WidthCm = 50m,
            HeightCm = 60m,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        product.ChannelProducts.Add(new ChannelProduct
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ChannelId = channelId,
            ExternalProductId = "1088660899",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            Channel = new Channel
            {
                Id = channelId,
                Name = "Ozon",
                Code = "ozon",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            },
        });

        _repositoryMock
            .Setup(r => r.GetByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        GetProductResponse response = await _handler.Handle(
            new GetProductQuery { Id = product.Id },
            CancellationToken.None);

        response.PackingUnit.Should().BeNull("a product may have no canonical row at all");
        response.ChannelLinks!.Should().ContainSingle().Which.PackingUnit!.LengthCm.Should().Be(40m);
    }

    [Fact]
    public async Task Handle_ChannelLinkCarriesItsBarcodesAndPhotos()
    {
        Guid channelId = Guid.NewGuid();
        Product product = BuildProduct();

        ChannelProduct link = new ChannelProduct
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ChannelId = channelId,
            ExternalProductId = "1088660899",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            Channel = new Channel
            {
                Id = channelId,
                Name = "Wildberries",
                Code = "wildberries",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            },
        };

        link.Barcodes.Add(new ChannelProductBarcode
        {
            Id = Guid.NewGuid(),
            ChannelProductId = link.Id,
            Value = "4600000000001",
            Type = "wildberries",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        link.Barcodes.Add(new ChannelProductBarcode
        {
            Id = Guid.NewGuid(),
            ChannelProductId = link.Id,
            Value = "4600000000002",
            Type = "EAN13",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        link.Photos.Add(BuildChannelPhoto(1, "large", "product-photos/1-large.webp", ProductPhotoStatus.Uploaded));
        link.Photos.Add(BuildChannelPhoto(1, "thumbnail", "product-photos/1-thumb.webp", ProductPhotoStatus.Uploaded));
        link.Photos.Add(BuildChannelPhoto(2, "thumbnail", "product-photos/2-thumb.webp", ProductPhotoStatus.Uploaded));
        link.Photos.Add(BuildChannelPhoto(3, "thumbnail", "product-photos/3-removed.webp", ProductPhotoStatus.Removed));

        product.ChannelProducts.Add(link);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        GetProductResponse response = await _handler.Handle(
            new GetProductQuery { Id = product.Id },
            CancellationToken.None);

        ProductChannelLinkDto dto = response.ChannelLinks.Should().ContainSingle().Subject;

        dto.Barcodes.Should().HaveCount(2);
        dto.Barcodes!.Select(b => b.Type).Should().BeEquivalentTo(new[] { "wildberries", "EAN13" });
        dto.Barcodes!.Select(b => b.Value).Should().BeEquivalentTo(new[] { "4600000000001", "4600000000002" });

        dto.Photos.Should().HaveCount(3, "a removed photo is not shown");
        dto.Photos!.Select(p => p.DisplayOrder).Should().Equal(1, 1, 2);
        dto.Photos!.Select(p => p.SizeVariant).Should().Equal("thumbnail", "large", "thumbnail");
        dto.Photos!.Should().OnlyContain(p => p.Url.StartsWith("https://cdn.test/", StringComparison.Ordinal));
    }

    private static ChannelProductPhoto BuildChannelPhoto(
        int displayOrder,
        string sizeVariant,
        string storageKey,
        ProductPhotoStatus status)
    {
        return new ChannelProductPhoto
        {
            Id = Guid.NewGuid(),
            ExternalId = $"{displayOrder}_{sizeVariant}",
            DisplayOrder = displayOrder,
            SizeVariant = sizeVariant,
            StorageKey = storageKey,
            ContentType = "image/webp",
            SizeBytes = 128,
            Source = "Wildberries",
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
