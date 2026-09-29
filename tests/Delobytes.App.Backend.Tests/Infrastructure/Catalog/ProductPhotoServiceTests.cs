using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Catalog.Infrastructure.Storage;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Contracts.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure.Catalog;

/// <summary>
/// Tests for <see cref="ProductPhotoService"/>. The object storage and the http client factory
/// are mocked, so the tests exercise the real pipeline — download, WebP sniffing, size limit,
/// key building — without any network or bucket.
/// </summary>
public class ProductPhotoServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly byte[] ValidWebP = BuildWebPWithFiller(0x5A);

    // ---------- WebP validation ----------

    [Fact]
    public void HasValidHeader_WithProperRiffWebPHeader_ReturnsTrue()
    {
        using MemoryStream stream = new MemoryStream(ValidWebP);

        WebPValidator.HasValidHeader(stream).Should().BeTrue();
    }

    [Fact]
    public void HasValidHeader_WithFileShorterThanHeader_ReturnsFalse()
    {
        using MemoryStream stream = new MemoryStream(new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00 });

        WebPValidator.HasValidHeader(stream).Should().BeFalse();
    }

    [Fact]
    public void HasValidHeader_WithCorruptedSignature_ReturnsFalse()
    {
        byte[] corrupted = BuildWebPWithFiller(0x5A);
        corrupted[8] = (byte)'X';

        using MemoryStream stream = new MemoryStream(corrupted);

        WebPValidator.HasValidHeader(stream).Should().BeFalse();
    }

    [Fact]
    public void HasValidHeader_WithPngPayload_ReturnsFalse()
    {
        byte[] png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D };

        using MemoryStream stream = new MemoryStream(png);

        WebPValidator.HasValidHeader(stream).Should().BeFalse();
    }

    [Fact]
    public void HasValidHeader_WithEmptyStream_ReturnsFalse()
    {
        using MemoryStream stream = new MemoryStream();

        WebPValidator.HasValidHeader(stream).Should().BeFalse();
    }

    [Fact]
    public void HasValidHeader_SetsPositionBackToStart()
    {
        using MemoryStream stream = new MemoryStream(ValidWebP);

        WebPValidator.HasValidHeader(stream);

        stream.Position.Should().Be(0);
    }

    [Fact]
    public void HasValidHeader_WithNonSeekableStream_StillReadsHeader()
    {
        using NonSeekableStream stream = new NonSeekableStream(ValidWebP);

        WebPValidator.HasValidHeader(stream).Should().BeTrue();
    }

    // ---------- Key convention ----------

    [Fact]
    public void BuildKey_FollowsProductPhotosConvention()
    {
        Guid productId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        Guid photoId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        string key = ProductPhotoKeys.Build(TenantId, productId, photoId);

        key.Should().Be(
            "product-photos/11111111111111111111111111111111/22222222222222222222222222222222/33333333333333333333333333333333.webp");
    }

    [Fact]
    public void BuildKey_IsUniquePerPhoto()
    {
        Guid productId = Guid.NewGuid();

        string first = ProductPhotoKeys.Build(TenantId, productId, Guid.NewGuid());
        string second = ProductPhotoKeys.Build(TenantId, productId, Guid.NewGuid());

        first.Should().NotBe(second);
    }

    // ---------- ImportPhotosAsync ----------

    [Fact]
    public async Task ImportPhotosAsync_WithAlreadyUploadedExternalId_SkipsAndDoesNotUpload()
    {
        Product product = BuildProduct();
        product.Photos.Add(new ProductPhoto
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ExternalId = "1_1_thumbnail",
            SizeVariant = "thumbnail",
            DisplayOrder = 1,
            StorageKey = "product-photos/existing.webp",
            OriginalFileName = "existing.webp",
            ContentType = "image/webp",
            Source = "Wildberries",
            Status = ProductPhotoStatus.Uploaded,
            CreatedAt = DateTimeOffset.UtcNow
        });

        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductPhotoService service = BuildService(storage, ValidWebP);

        ProductPhotoImportResult result = await service.ImportPhotosAsync(
            product,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        result.Imported.Should().Be(0);
        result.Skipped.Should().Be(1);
        result.Failed.Should().Be(0);
        product.Photos.Should().HaveCount(1);

        storage.Verify(
            s => s.UploadAsync(
                It.IsAny<StorageArea>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ImportPhotosAsync_WhenUploadThrows_CountsFailureAndContinuesWithRemainingSources()
    {
        Product product = BuildProduct();

        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();

        storage
            .Setup(s => s.UploadAsync(
                It.IsAny<StorageArea>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ObjectStorageException("bucket unavailable"));

        ProductPhotoService service = BuildService(storage, ValidWebP, ValidWebP);

        MarketplacePhotoSource[] sources =
        {
            BuildSource("1_1_thumbnail", 1, "thumbnail"),
            BuildSource("1_1_large", 1, "large")
        };

        ProductPhotoImportResult result = await service.ImportPhotosAsync(product, sources, CancellationToken.None);

        result.Failed.Should().Be(2);
        result.Imported.Should().Be(0);
        result.Skipped.Should().Be(0);

        product.Photos.Should().HaveCount(2);
        product.Photos.Should().OnlyContain(p => p.Status == ProductPhotoStatus.Failed);
        product.Photos.Should().OnlyContain(p => p.ErrorMessage != null);

        storage.Verify(
            s => s.UploadAsync(
                It.IsAny<StorageArea>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ImportPhotosAsync_WhenOneSourceFails_StillImportsTheOthers()
    {
        Product product = BuildProduct();

        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();

        int call = 0;
        storage
            .Setup(s => s.UploadAsync(
                It.IsAny<StorageArea>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                call++;

                if (call == 1)
                {
                    throw new ObjectStorageException("transient failure");
                }

                return Task.CompletedTask;
            });

        ProductPhotoService service = BuildService(storage, ValidWebP, ValidWebP);

        MarketplacePhotoSource[] sources =
        {
            BuildSource("1_1_thumbnail", 1, "thumbnail"),
            BuildSource("1_1_large", 1, "large")
        };

        ProductPhotoImportResult result = await service.ImportPhotosAsync(product, sources, CancellationToken.None);

        result.Failed.Should().Be(1);
        result.Imported.Should().Be(1);
        product.Photos.Should().ContainSingle(p => p.Status == ProductPhotoStatus.Uploaded);
        product.Photos.Should().ContainSingle(p => p.Status == ProductPhotoStatus.Failed);
    }

    [Fact]
    public async Task ImportPhotosAsync_AddsRowsWithStorageMetadata()
    {
        Product product = BuildProduct();
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();

        ProductPhotoService service = BuildService(storage, ValidWebP);

        await service.ImportPhotosAsync(
            product,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        ProductPhoto photo = product.Photos.Should().ContainSingle().Subject;

        photo.ProductId.Should().Be(product.Id);
        photo.ExternalId.Should().Be("1_1_thumbnail");
        photo.DisplayOrder.Should().Be(1);
        photo.SizeVariant.Should().Be("thumbnail");
        photo.Source.Should().Be("Wildberries");
        photo.Status.Should().Be(ProductPhotoStatus.Uploaded);
        photo.ContentType.Should().Be("image/webp");
        photo.SizeBytes.Should().Be(ValidWebP.Length);
        photo.OriginalFileName.Should().Be($"{photo.Id:N}.webp");
        photo.StorageKey.Should().Be(ProductPhotoKeys.Build(TenantId, product.Id, photo.Id));
        photo.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ImportPhotosAsync_WithInvalidWebP_FailsWithoutUploading()
    {
        Product product = BuildProduct();
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();

        byte[] notWebP = new byte[64];
        notWebP[0] = 0x47;

        ProductPhotoService service = BuildService(storage, notWebP);

        ProductPhotoImportResult result = await service.ImportPhotosAsync(
            product,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        result.Failed.Should().Be(1);
        result.Imported.Should().Be(0);

        storage.Verify(
            s => s.UploadAsync(
                It.IsAny<StorageArea>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ImportPhotosAsync_SkipsDuplicateExternalIdWithinOneBatch()
    {
        Product product = BuildProduct();
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();

        ProductPhotoService service = BuildService(storage, ValidWebP, ValidWebP);

        MarketplacePhotoSource[] sources =
        {
            BuildSource("1_1_thumbnail", 1, "thumbnail"),
            BuildSource("1_1_thumbnail", 1, "thumbnail")
        };

        ProductPhotoImportResult result = await service.ImportPhotosAsync(product, sources, CancellationToken.None);

        result.Imported.Should().Be(1);
        result.Skipped.Should().Be(1);
        product.Photos.Should().HaveCount(1);
    }

    [Fact]
    public async Task ImportPhotosAsync_WithPreviouslyFailedPhoto_ReusesTheSameRow()
    {
        Product product = BuildProduct();
        Guid existingPhotoId = Guid.NewGuid();

        product.Photos.Add(new ProductPhoto
        {
            Id = existingPhotoId,
            ProductId = product.Id,
            ExternalId = "1_1_thumbnail",
            SizeVariant = "thumbnail",
            DisplayOrder = 1,
            StorageKey = ProductPhotoKeys.Build(TenantId, product.Id, existingPhotoId),
            OriginalFileName = "old.webp",
            ContentType = "image/webp",
            Source = "Wildberries",
            Status = ProductPhotoStatus.Failed,
            ErrorMessage = "Upload to object storage failed",
            CreatedAt = DateTimeOffset.UtcNow
        });

        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductPhotoService service = BuildService(storage, ValidWebP);

        ProductPhotoImportResult result = await service.ImportPhotosAsync(
            product,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        result.Imported.Should().Be(1);
        result.Skipped.Should().Be(0);

        ProductPhoto photo = product.Photos.Should().ContainSingle().Subject;

        // Reusing the row keeps its id, which keeps the storage key — and therefore the blob —
        // stable across retries.
        photo.Id.Should().Be(existingPhotoId);
        photo.Status.Should().Be(ProductPhotoStatus.Uploaded);
        photo.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ImportPhotosAsync_WithEmptySourceList_DoesNothing()
    {
        Product product = BuildProduct();
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();

        ProductPhotoService service = BuildService(storage);

        ProductPhotoImportResult result = await service.ImportPhotosAsync(
            product,
            Array.Empty<MarketplacePhotoSource>(),
            CancellationToken.None);

        result.Imported.Should().Be(0);
        result.Skipped.Should().Be(0);
        result.Failed.Should().Be(0);
    }

    [Fact]
    public async Task ImportPhotosAsync_WithoutTenant_Throws()
    {
        Product product = BuildProduct();
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();

        ProductPhotoService service = BuildServiceWith(storage, null, HttpStatusCode.OK, ValidWebP);

        Func<Task> act = async () => await service.ImportPhotosAsync(
            product,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ImportPhotosAsync_WithOversizedPayload_FailsWithoutUploading()
    {
        Product product = BuildProduct();
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();

        // Just over the 10 MiB limit, with an otherwise valid WebP header.
        byte[] tooLarge = BuildWebP(10 * 1024 * 1024 + 1);

        ProductPhotoService service = BuildService(storage, tooLarge);

        ProductPhotoImportResult result = await service.ImportPhotosAsync(
            product,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        result.Failed.Should().Be(1);

        storage.Verify(
            s => s.UploadAsync(
                It.IsAny<StorageArea>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ImportPhotosAsync_WhenDownloadReturnsErrorStatus_FailsWithoutUploading()
    {
        Product product = BuildProduct();
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();

        ProductPhotoService service = BuildServiceWith(storage, TenantId, HttpStatusCode.NotFound, ValidWebP);

        ProductPhotoImportResult result = await service.ImportPhotosAsync(
            product,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        result.Failed.Should().Be(1);
        product.Photos.Should().ContainSingle(p => p.ErrorMessage != null);
    }

    // ---------- Delete / URL ----------

    [Fact]
    public async Task DeletePhotosAsync_DeletesExactlyTheGivenKeys()
    {
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductPhotoService service = BuildService(storage);

        ProductPhoto first = BuildStoredPhoto("product-photos/a.webp");
        ProductPhoto second = BuildStoredPhoto("product-photos/b.webp");

        await service.DeletePhotosAsync(new[] { first, second }, CancellationToken.None);

        storage.Verify(
            s => s.DeleteManyAsync(
                StorageArea.ProductPhotos,
                It.Is<IReadOnlyCollection<string>>(keys =>
                    keys.Count == 2
                    && keys.Contains("product-photos/a.webp")
                    && keys.Contains("product-photos/b.webp")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeletePhotosAsync_WithEmptyList_DoesNotTouchStorage()
    {
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductPhotoService service = BuildService(storage);

        await service.DeletePhotosAsync(Array.Empty<ProductPhoto>(), CancellationToken.None);

        storage.Verify(
            s => s.DeleteManyAsync(
                It.IsAny<StorageArea>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void GetPublicUrl_DelegatesToStorage()
    {
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        storage
            .Setup(s => s.GetPublicUrl(StorageArea.ProductPhotos, "product-photos/a.webp"))
            .Returns("https://cdn.example.com/product-photos/a.webp");

        ProductPhotoService service = BuildService(storage);

        string url = service.GetPublicUrl(BuildStoredPhoto("product-photos/a.webp"));

        url.Should().Be("https://cdn.example.com/product-photos/a.webp");
    }

    // ---------- helpers ----------

    private static ProductPhotoService BuildService(Mock<IObjectStorage> storage, params byte[][] payloads)
    {
        return BuildServiceWith(storage, TenantId, HttpStatusCode.OK, payloads);
    }

    private static ProductPhotoService BuildServiceWith(
        Mock<IObjectStorage> storage,
        Guid? tenantId,
        HttpStatusCode statusCode,
        params byte[][] payloads)
    {
        Queue<byte[]> queue = new Queue<byte[]>(payloads);

        HttpClient httpClient = new HttpClient(new StubHttpMessageHandler(() => new HttpResponseMessage(statusCode)
        {
            Content = new ByteArrayContent(queue.Count > 0 ? queue.Dequeue() : Array.Empty<byte>())
        }));

        Mock<IHttpClientFactory> httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(f => f.CreateClient(ProductPhotoService.HttpClientName))
            .Returns(httpClient);

        Mock<ITenantContext> tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.TenantId).Returns(tenantId);

        return new ProductPhotoService(
            storage.Object,
            httpClientFactory.Object,
            tenantContext.Object,
            Mock.Of<ILogger<ProductPhotoService>>());
    }

    private static MarketplacePhotoSource BuildSource(string externalId, int displayOrder, string sizeVariant)
    {
        return new MarketplacePhotoSource
        {
            Url = "https://basket-01.wbbasket.ru/vol0/part0/" + externalId + ".webp",
            ExternalId = externalId,
            DisplayOrder = displayOrder,
            SizeVariant = sizeVariant,
            Width = 516,
            Height = 688
        };
    }

    private static ProductPhoto BuildStoredPhoto(string storageKey)
    {
        return new ProductPhoto
        {
            Id = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ExternalId = Guid.NewGuid().ToString("N"),
            SizeVariant = "thumbnail",
            StorageKey = storageKey,
            OriginalFileName = "photo.webp",
            ContentType = "image/webp",
            Source = "Wildberries",
            Status = ProductPhotoStatus.Uploaded,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static Product BuildProduct()
    {
        return new Product
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-" + Guid.NewGuid().ToString("N")[..8],
            Name = "Товар",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>Builds a payload that starts with a valid RIFF/WEBP header and is <paramref name="length"/> bytes long.</summary>
    private static byte[] BuildWebP(int length)
    {
        byte[] payload = new byte[length];
        payload[0] = (byte)'R';
        payload[1] = (byte)'I';
        payload[2] = (byte)'F';
        payload[3] = (byte)'F';
        payload[8] = (byte)'W';
        payload[9] = (byte)'E';
        payload[10] = (byte)'B';
        payload[11] = (byte)'P';

        return payload;
    }

    /// <summary>Valid WebP payload with a distinguishing filler byte, so queued payloads differ.</summary>
    private static byte[] BuildWebPWithFiller(byte filler)
    {
        byte[] payload = BuildWebP(64);
        payload[4] = filler;

        return payload;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory());
        }
    }

    private sealed class NonSeekableStream : Stream
    {
        private readonly MemoryStream _inner;

        public NonSeekableStream(byte[] content)
        {
            _inner = new MemoryStream(content);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _inner.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
