using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Catalog.Application.Services;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Contracts.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog;

/// <summary>
/// Tests for <see cref="ProductPhotoService"/>. The object storage and the http client factory
/// are mocked, so the tests exercise the real pipeline — download, WebP sniffing, size limit,
/// hashing, key building, supersede and reconciliation — without any network or bucket.
/// </summary>
public class ProductPhotoServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ChannelId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid OtherChannelId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static readonly byte[] ValidWebP = BuildWebPWithFiller(0x5A);
    private static readonly byte[] OtherWebP = BuildWebPWithFiller(0x6B);

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
    public void BuildKey_IsContentAddressed()
    {
        Guid productId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        string contentHash = Sha256Hex(ValidWebP);

        string key = ProductPhotoKeys.Build(TenantId, productId, contentHash);

        key.Should().Be(
            $"product-photos/11111111111111111111111111111111/22222222222222222222222222222222/{contentHash}.webp");
    }

    [Fact]
    public void BuildKey_IsStableForTheSameContent()
    {
        Guid productId = Guid.NewGuid();
        string contentHash = Sha256Hex(ValidWebP);

        string first = ProductPhotoKeys.Build(TenantId, productId, contentHash);
        string second = ProductPhotoKeys.Build(TenantId, productId, contentHash);

        first.Should().Be(second, "identical bytes must resolve to one object");
    }

    [Fact]
    public void BuildKey_IsUniquePerContent()
    {
        Guid productId = Guid.NewGuid();

        string first = ProductPhotoKeys.Build(TenantId, productId, Sha256Hex(ValidWebP));
        string second = ProductPhotoKeys.Build(TenantId, productId, Sha256Hex(OtherWebP));

        first.Should().NotBe(second);
    }

    // ---------- ImportPhotosAsync: unchanged content ----------

    [Fact]
    public async Task ImportPhotosAsync_WithMatchingContentHash_SkipsWithoutUpload()
    {
        Product product = BuildProduct();
        BuildUploadedPhoto(
            product,
            "1_1_thumbnail",
            ChannelId,
            Sha256Hex(ValidWebP),
            "product-photos/existing.webp");

        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductPhotoService service = BuildService(storage, ValidWebP);

        ProductPhotoImportResult result = await service.ImportPhotosAsync(
            product,
            ChannelId,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        result.Imported.Should().Be(0);
        result.Replaced.Should().Be(0);
        result.Skipped.Should().Be(1);
        result.Failed.Should().Be(0);
        product.Photos.Should().HaveCount(1);
        product.Photos.Single().Status.Should().Be(ProductPhotoStatus.Uploaded);
        product.Photos.Single().LastCheckedAt.Should().NotBeNull();

        VerifyNoUpload(storage);
    }

    // ---------- ImportPhotosAsync: adoption of legacy rows ----------

    [Fact]
    public async Task ImportPhotosAsync_WithLegacyRowWithoutHash_AdoptsItInPlace()
    {
        Product product = BuildProduct();

        ProductPhoto legacy = BuildUploadedPhoto(
            product,
            "1_1_thumbnail",
            Guid.Empty,
            null,
            "product-photos/legacy-by-photo-id.webp");

        legacy.SizeBytes = 4242;
        legacy.Width = 246;
        legacy.Height = 328;

        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductPhotoService service = BuildService(storage, ValidWebP);

        ProductPhotoImportResult result = await service.ImportPhotosAsync(
            product,
            ChannelId,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        result.Skipped.Should().Be(1);
        result.Imported.Should().Be(0);
        result.Replaced.Should().Be(0);

        ProductPhoto adopted = product.Photos.Should().ContainSingle().Subject;

        adopted.Id.Should().Be(legacy.Id);
        adopted.ContentHash.Should().Be(Sha256Hex(ValidWebP));
        adopted.ChannelId.Should().Be(ChannelId);

        // The object at the persisted key is by construction the bytes just hashed.
        adopted.StorageKey.Should().Be("product-photos/legacy-by-photo-id.webp");
        adopted.SizeBytes.Should().Be(4242);
        adopted.Width.Should().Be(246);
        adopted.Status.Should().Be(ProductPhotoStatus.Uploaded);

        VerifyNoUpload(storage);
    }

    // ---------- ImportPhotosAsync: replacement ----------

    [Fact]
    public async Task ImportPhotosAsync_WhenContentChanged_SupersedesTheOldRow()
    {
        Product product = BuildProduct();

        ProductPhoto previous = BuildUploadedPhoto(
            product,
            "1_1_thumbnail",
            ChannelId,
            Sha256Hex(ValidWebP),
            ProductPhotoKeys.Build(TenantId, product.Id, Sha256Hex(ValidWebP)));

        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductPhotoService service = BuildService(storage, OtherWebP);

        ProductPhotoImportResult result = await service.ImportPhotosAsync(
            product,
            ChannelId,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        result.Replaced.Should().Be(1);
        result.Imported.Should().Be(0);
        result.Skipped.Should().Be(0);

        product.Photos.Should().HaveCount(2);

        previous.Status.Should().Be(ProductPhotoStatus.Removed);
        previous.RemovedAt.Should().NotBeNull();

        ProductPhoto current = product.Photos
            .Should().ContainSingle(p => p.Status == ProductPhotoStatus.Uploaded).Subject;

        current.Id.Should().NotBe(previous.Id);
        current.ContentHash.Should().Be(Sha256Hex(OtherWebP));
        current.StorageKey.Should().Be(ProductPhotoKeys.Build(TenantId, product.Id, Sha256Hex(OtherWebP)));
        current.StorageKey.Should().NotBe(previous.StorageKey);

        product.Photos
            .Count(p => p.Status != ProductPhotoStatus.Removed && p.ExternalId == "1_1_thumbnail")
            .Should().Be(1, "a slot has exactly one live version");
    }

    // ---------- ImportPhotosAsync: addition ----------

    [Fact]
    public async Task ImportPhotosAsync_WithNewSlot_ImportsIt()
    {
        Product product = BuildProduct();

        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductPhotoService service = BuildService(storage, ValidWebP);

        ProductPhotoImportResult result = await service.ImportPhotosAsync(
            product,
            ChannelId,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        result.Imported.Should().Be(1);
        result.Replaced.Should().Be(0);
        result.Skipped.Should().Be(0);
        result.Removed.Should().Be(0);

        product.Photos.Should().ContainSingle();
        product.Photos.Single().Status.Should().Be(ProductPhotoStatus.Uploaded);
    }

    // ---------- ImportPhotosAsync: removal ----------

    [Fact]
    public async Task ImportPhotosAsync_WhenSlotIsNoLongerDeclared_MarksItRemoved()
    {
        Product product = BuildProduct();

        ProductPhoto kept = BuildUploadedPhoto(
            product,
            "1_1_thumbnail",
            ChannelId,
            Sha256Hex(ValidWebP),
            "product-photos/kept.webp");

        ProductPhoto dropped = BuildUploadedPhoto(
            product,
            "1_2_thumbnail",
            ChannelId,
            Sha256Hex(OtherWebP),
            "product-photos/dropped.webp");

        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductPhotoService service = BuildService(storage, ValidWebP);

        ProductPhotoImportResult result = await service.ImportPhotosAsync(
            product,
            ChannelId,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        result.Removed.Should().Be(1);
        result.Skipped.Should().Be(1);

        kept.Status.Should().Be(ProductPhotoStatus.Uploaded);
        dropped.Status.Should().Be(ProductPhotoStatus.Removed);
        dropped.RemovedAt.Should().NotBeNull();

        // Reconciliation marks rows; it never deletes blobs, that is the sweeper's job.
        storage.Verify(
            s => s.DeleteManyAsync(
                It.IsAny<StorageArea>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ImportPhotosAsync_WithEmptyDeclaration_RemovesEveryLivePhotoOfTheChannel()
    {
        Product product = BuildProduct();
        BuildUploadedPhoto(
            product,
            "1_1_thumbnail",
            ChannelId,
            Sha256Hex(ValidWebP),
            "product-photos/only.webp");

        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductPhotoService service = BuildService(storage);

        ProductPhotoImportResult result = await service.ImportPhotosAsync(
            product,
            ChannelId,
            Array.Empty<MarketplacePhotoSource>(),
            CancellationToken.None);

        result.Removed.Should().Be(1);
        result.Imported.Should().Be(0);
        result.Skipped.Should().Be(0);
        result.Failed.Should().Be(0);

        product.Photos.Should().ContainSingle()
            .Which.Status.Should().Be(ProductPhotoStatus.Removed);
    }

    [Fact]
    public async Task ImportPhotosAsync_WithEmptyDeclaration_DoesNotTouchOtherChannels()
    {
        Product product = BuildProduct();
        BuildUploadedPhoto(
            product,
            "1_1_thumbnail",
            OtherChannelId,
            Sha256Hex(OtherWebP),
            "product-photos/other-channel.webp");

        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductPhotoService service = BuildService(storage);

        ProductPhotoImportResult result = await service.ImportPhotosAsync(
            product,
            ChannelId,
            Array.Empty<MarketplacePhotoSource>(),
            CancellationToken.None);

        result.Removed.Should().Be(0);
        product.Photos.Should().ContainSingle()
            .Which.Status.Should().Be(ProductPhotoStatus.Uploaded);
    }

    // ---------- ImportPhotosAsync: channel scoping ----------

    [Fact]
    public async Task ImportPhotosAsync_TwoChannelsDeclaringTheSameSlot_KeepSeparateLiveRows()
    {
        Product product = BuildProduct();
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();

        ProductPhotoService service = BuildService(storage, ValidWebP, OtherWebP);

        MarketplacePhotoSource[] sources = { BuildSource("1_1_thumbnail", 1, "thumbnail") };

        ProductPhotoImportResult first = await service.ImportPhotosAsync(
            product, ChannelId, sources, CancellationToken.None);

        ProductPhotoImportResult second = await service.ImportPhotosAsync(
            product, OtherChannelId, sources, CancellationToken.None);

        first.Imported.Should().Be(1);
        second.Imported.Should().Be(1, "the slot key is scoped by channel, not global");

        List<ProductPhoto> live = product.Photos
            .Where(p => p.Status == ProductPhotoStatus.Uploaded)
            .ToList();

        live.Should().HaveCount(2);
        live.Select(p => p.ChannelId).Should().BeEquivalentTo(new[] { ChannelId, OtherChannelId });
        live.Select(p => p.StorageKey).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ImportPhotosAsync_TwoChannelsWithIdenticalBytes_ShareOneStorageKey()
    {
        Product product = BuildProduct();
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();

        ProductPhotoService service = BuildService(storage, ValidWebP, ValidWebP);

        MarketplacePhotoSource[] sources = { BuildSource("1_1_thumbnail", 1, "thumbnail") };

        await service.ImportPhotosAsync(product, ChannelId, sources, CancellationToken.None);
        await service.ImportPhotosAsync(product, OtherChannelId, sources, CancellationToken.None);

        List<ProductPhoto> live = product.Photos
            .Where(p => p.Status == ProductPhotoStatus.Uploaded)
            .ToList();

        live.Should().HaveCount(2);
        live.Select(p => p.StorageKey).Distinct().Should().ContainSingle();

        // Uploaded once per run, both times to the same content-addressed key: the second channel
        // costs no extra object, which is what content addressing buys across channels.
        storage.Verify(
            s => s.UploadAsync(
                StorageArea.ProductPhotos,
                It.Is<string>(key => key == live[0].StorageKey),
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    // ---------- ImportPhotosAsync: failures ----------

    [Fact]
    public async Task ImportPhotosAsync_WhenDownloadFails_KeepsTheGoodPhotoUploaded()
    {
        Product product = BuildProduct();

        ProductPhoto existing = BuildUploadedPhoto(
            product,
            "1_1_thumbnail",
            ChannelId,
            Sha256Hex(ValidWebP),
            "product-photos/good.webp");

        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductPhotoService service = BuildServiceWithStatus(storage, HttpStatusCode.NotFound);

        ProductPhotoImportResult result = await service.ImportPhotosAsync(
            product,
            ChannelId,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        result.Failed.Should().Be(1);
        result.Imported.Should().Be(0);

        product.Photos.Should().ContainSingle();

        existing.Status.Should().Be(ProductPhotoStatus.Uploaded, "a CDN hiccup must not erase a served photo");
        existing.ErrorMessage.Should().NotBeNull();
        existing.LastCheckedAt.Should().NotBeNull();

        VerifyNoUpload(storage);
    }

    [Fact]
    public async Task ImportPhotosAsync_WhenFirstDownloadFails_CreatesAFailedRow()
    {
        Product product = BuildProduct();
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();

        ProductPhotoService service = BuildServiceWithStatus(storage, HttpStatusCode.NotFound);

        ProductPhotoImportResult result = await service.ImportPhotosAsync(
            product,
            ChannelId,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        result.Failed.Should().Be(1);

        ProductPhoto photo = product.Photos.Should().ContainSingle().Subject;

        photo.Status.Should().Be(ProductPhotoStatus.Failed);
        photo.ChannelId.Should().Be(ChannelId);
        photo.ErrorMessage.Should().NotBeNull();
        photo.ContentHash.Should().BeNull();

        VerifyNoUpload(storage);
    }

    [Fact]
    public async Task ImportPhotosAsync_WhenUploadFails_KeepsThePriorPhotoAndCreatesNoOrphan()
    {
        Product product = BuildProduct();

        ProductPhoto existing = BuildUploadedPhoto(
            product,
            "1_1_thumbnail",
            ChannelId,
            Sha256Hex(ValidWebP),
            "product-photos/good.webp");

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

        ProductPhotoService service = BuildService(storage, OtherWebP);

        ProductPhotoImportResult result = await service.ImportPhotosAsync(
            product,
            ChannelId,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        result.Failed.Should().Be(1);
        result.Replaced.Should().Be(0);

        product.Photos.Should().ContainSingle("a failed replacement must not add a row");
        existing.Status.Should().Be(ProductPhotoStatus.Uploaded);
        existing.ErrorMessage.Should().NotBeNull();
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

        ProductPhotoImportResult result = await service.ImportPhotosAsync(product, ChannelId, sources, CancellationToken.None);

        result.Failed.Should().Be(2);
        result.Imported.Should().Be(0);
        result.Skipped.Should().Be(0);

        product.Photos.Should().HaveCount(2);
        product.Photos.Should().OnlyContain(p => p.Status == ProductPhotoStatus.Failed);
        product.Photos.Should().OnlyContain(p => p.ErrorMessage != null);
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

        ProductPhotoService service = BuildService(storage, ValidWebP, OtherWebP);

        MarketplacePhotoSource[] sources =
        {
            BuildSource("1_1_thumbnail", 1, "thumbnail"),
            BuildSource("1_1_large", 1, "large")
        };

        ProductPhotoImportResult result = await service.ImportPhotosAsync(product, ChannelId, sources, CancellationToken.None);

        result.Failed.Should().Be(1);
        result.Imported.Should().Be(1);
        product.Photos.Should().ContainSingle(p => p.Status == ProductPhotoStatus.Uploaded);
        product.Photos.Should().ContainSingle(p => p.Status == ProductPhotoStatus.Failed);
    }

    // ---------- ImportPhotosAsync: duplicates, tenant, validation ----------

    [Fact]
    public async Task ImportPhotosAsync_SkipsDuplicateExternalIdWithinOneBatch()
    {
        Product product = BuildProduct();
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();

        ProductPhotoService service = BuildService(storage, ValidWebP);

        MarketplacePhotoSource[] sources =
        {
            BuildSource("1_1_thumbnail", 1, "thumbnail"),
            BuildSource("1_1_thumbnail", 1, "thumbnail")
        };

        ProductPhotoImportResult result = await service.ImportPhotosAsync(product, ChannelId, sources, CancellationToken.None);

        result.Imported.Should().Be(1);
        result.Skipped.Should().Be(1);
        product.Photos.Should().HaveCount(1);
    }

    [Fact]
    public async Task ImportPhotosAsync_WithoutTenant_Throws()
    {
        Product product = BuildProduct();
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();

        ProductPhotoService service = BuildServiceWith(storage, null, HttpStatusCode.OK, ValidWebP);

        Func<Task> act = async () => await service.ImportPhotosAsync(
            product,
            ChannelId,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
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
            ChannelId,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        result.Failed.Should().Be(1);
        VerifyNoUpload(storage);
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
            ChannelId,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        result.Failed.Should().Be(1);
        VerifyNoUpload(storage);
    }

    [Fact]
    public async Task ImportPhotosAsync_WhenDownloadReturnsErrorStatus_FailsWithoutUploading()
    {
        Product product = BuildProduct();
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();

        ProductPhotoService service = BuildServiceWith(storage, TenantId, HttpStatusCode.NotFound, ValidWebP);

        ProductPhotoImportResult result = await service.ImportPhotosAsync(
            product,
            ChannelId,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        result.Failed.Should().Be(1);
        product.Photos.Should().ContainSingle(p => p.ErrorMessage != null);
    }

    [Fact]
    public async Task ImportPhotosAsync_AddsRowsWithStorageMetadata()
    {
        Product product = BuildProduct();
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();

        ProductPhotoService service = BuildService(storage, ValidWebP);

        await service.ImportPhotosAsync(
            product,
            ChannelId,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        ProductPhoto photo = product.Photos.Should().ContainSingle().Subject;

        photo.ProductId.Should().Be(product.Id);
        photo.ChannelId.Should().Be(ChannelId);
        photo.ExternalId.Should().Be("1_1_thumbnail");
        photo.DisplayOrder.Should().Be(1);
        photo.SizeVariant.Should().Be("thumbnail");
        photo.Source.Should().Be("Wildberries");
        photo.Status.Should().Be(ProductPhotoStatus.Uploaded);
        photo.ContentType.Should().Be("image/webp");
        photo.ContentHash.Should().Be(Sha256Hex(ValidWebP));
        photo.SizeBytes.Should().Be(ValidWebP.Length);
        photo.OriginalFileName.Should().Be($"{photo.Id:N}.webp");
        photo.StorageKey.Should().Be(ProductPhotoKeys.Build(TenantId, product.Id, Sha256Hex(ValidWebP)));
        photo.LastCheckedAt.Should().NotBeNull();
        photo.ErrorMessage.Should().BeNull();
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

    private static void VerifyNoUpload(Mock<IObjectStorage> storage)
    {
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

    private static string Sha256Hex(byte[] payload)
    {
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }

    private static ProductPhotoService BuildService(Mock<IObjectStorage> storage, params byte[][] payloads)
    {
        return BuildServiceWith(storage, TenantId, HttpStatusCode.OK, payloads);
    }

    private static ProductPhotoService BuildServiceWithStatus(Mock<IObjectStorage> storage, HttpStatusCode statusCode)
    {
        return BuildServiceWith(storage, TenantId, statusCode);
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

        return BuildServiceAround(storage, tenantId, httpClient);
    }

    private static ProductPhotoService BuildServiceAround(
        Mock<IObjectStorage> storage,
        Guid? tenantId,
        HttpClient httpClient)
    {
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
            Mock.Of<ILogger<ProductPhotoService>>(),
            TimeProvider.System);
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

    // Added to product.Photos by the builder: a row the service cannot see would turn every
    // pre-existing-row scenario into a silent addition case.
    private static ProductPhoto BuildUploadedPhoto(
        Product product,
        string externalId,
        Guid channelId,
        string? contentHash,
        string storageKey)
    {
        ProductPhoto photo = new ProductPhoto
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ChannelId = channelId,
            ExternalId = externalId,
            SizeVariant = "thumbnail",
            DisplayOrder = 1,
            ContentHash = contentHash,
            StorageKey = storageKey,
            OriginalFileName = "existing.webp",
            ContentType = "image/webp",
            SizeBytes = 64,
            Source = "Wildberries",
            Status = ProductPhotoStatus.Uploaded,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        product.Photos.Add(photo);

        return photo;
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
