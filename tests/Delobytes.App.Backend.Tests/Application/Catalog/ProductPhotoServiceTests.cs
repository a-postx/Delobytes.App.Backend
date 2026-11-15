using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Application.Services;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
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

    // ---------- ImportCanonicalPhotosAsync: unchanged content ----------

    [Fact]
    public async Task ImportCanonicalPhotosAsync_WithMatchingContentHash_SkipsWithoutUpload()
    {
        Product product = BuildProduct();
        BuildUploadedPhoto(
            product,
            "1_1_thumbnail",
            Sha256Hex(ValidWebP),
            "product-photos/existing.webp");

        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductPhotoService service = BuildService(storage, ValidWebP);

        ProductPhotoImportResult result = await service.ImportCanonicalPhotosAsync(
            product,
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

    // ---------- ImportCanonicalPhotosAsync: adoption of legacy rows ----------

    [Fact]
    public async Task ImportCanonicalPhotosAsync_WithLegacyRowWithoutHash_AdoptsItInPlace()
    {
        Product product = BuildProduct();

        ProductPhoto legacy = BuildUploadedPhoto(
            product,
            "1_1_thumbnail",
            null,
            "product-photos/legacy-by-photo-id.webp");

        legacy.SizeBytes = 4242;
        legacy.Width = 246;
        legacy.Height = 328;

        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductPhotoService service = BuildService(storage, ValidWebP);

        ProductPhotoImportResult result = await service.ImportCanonicalPhotosAsync(
            product,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        result.Skipped.Should().Be(1);
        result.Imported.Should().Be(0);
        result.Replaced.Should().Be(0);

        ProductPhoto adopted = product.Photos.Should().ContainSingle().Subject;

        adopted.Id.Should().Be(legacy.Id);
        adopted.ContentHash.Should().Be(Sha256Hex(ValidWebP));

        // The object at the persisted key is by construction the bytes just hashed.
        adopted.StorageKey.Should().Be("product-photos/legacy-by-photo-id.webp");
        adopted.SizeBytes.Should().Be(4242);
        adopted.Width.Should().Be(246);
        adopted.Status.Should().Be(ProductPhotoStatus.Uploaded);

        VerifyNoUpload(storage);
    }

    // ---------- ImportCanonicalPhotosAsync: replacement ----------

    [Fact]
    public async Task ImportCanonicalPhotosAsync_WhenContentChanged_SupersedesTheOldRow()
    {
        Product product = BuildProduct();

        ProductPhoto previous = BuildUploadedPhoto(
            product,
            "1_1_thumbnail",
            Sha256Hex(ValidWebP),
            ProductPhotoKeys.Build(TenantId, product.Id, Sha256Hex(ValidWebP)));

        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductPhotoService service = BuildService(storage, OtherWebP);

        ProductPhotoImportResult result = await service.ImportCanonicalPhotosAsync(
            product,
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

    // ---------- ImportCanonicalPhotosAsync: addition ----------

    [Fact]
    public async Task ImportCanonicalPhotosAsync_WithNewSlot_ImportsIt()
    {
        Product product = BuildProduct();

        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductPhotoService service = BuildService(storage, ValidWebP);

        ProductPhotoImportResult result = await service.ImportCanonicalPhotosAsync(
            product,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        result.Imported.Should().Be(1);
        result.Replaced.Should().Be(0);
        result.Skipped.Should().Be(0);
        result.Removed.Should().Be(0);

        product.Photos.Should().ContainSingle();
        product.Photos.Single().Status.Should().Be(ProductPhotoStatus.Uploaded);
    }

    // ---------- ImportCanonicalPhotosAsync: removal ----------

    [Fact]
    public async Task ImportCanonicalPhotosAsync_WhenSlotIsNoLongerDeclared_MarksItRemoved()
    {
        Product product = BuildProduct();

        ProductPhoto kept = BuildUploadedPhoto(
            product,
            "1_1_thumbnail",
            Sha256Hex(ValidWebP),
            "product-photos/kept.webp");

        ProductPhoto dropped = BuildUploadedPhoto(
            product,
            "1_2_thumbnail",
            Sha256Hex(OtherWebP),
            "product-photos/dropped.webp");

        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductPhotoService service = BuildService(storage, ValidWebP);

        ProductPhotoImportResult result = await service.ImportCanonicalPhotosAsync(
            product,
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
    public async Task ImportCanonicalPhotosAsync_WithEmptyDeclaration_RemovesEveryLivePhotoOfTheChannel()
    {
        Product product = BuildProduct();
        BuildUploadedPhoto(
            product,
            "1_1_thumbnail",
            Sha256Hex(ValidWebP),
            "product-photos/only.webp");

        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductPhotoService service = BuildService(storage);

        ProductPhotoImportResult result = await service.ImportCanonicalPhotosAsync(
            product,
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
    public async Task SyncChannelPhotosAsync_WithEmptyDeclaration_DoesNotTouchTheCanonicalGallery()
    {
        Product product = BuildProduct();
        BuildUploadedPhoto(
            product,
            "1_1_thumbnail",
            Sha256Hex(OtherWebP),
            "product-photos/canonical.webp");

        ChannelProduct link = BuildChannelProduct(product);
        BuildUploadedChannelPhoto(
            link,
            "1_1_thumbnail",
            Sha256Hex(OtherWebP),
            "product-photos/channel.webp");

        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductPhotoService service = BuildService(storage);

        ProductPhotoImportResult result = await service.SyncChannelPhotosAsync(
            link,
            Array.Empty<MarketplacePhotoSource>(),
            CancellationToken.None);

        result.Removed.Should().Be(1);
        product.Photos.Should().ContainSingle()
            .Which.Status.Should().Be(ProductPhotoStatus.Uploaded);
        link.Photos.Should().ContainSingle()
            .Which.Status.Should().Be(ProductPhotoStatus.Removed);
    }

    // ---------- SyncChannelPhotosAsync: channel scoping ----------

    [Fact]
    public async Task SyncChannelPhotosAsync_TwoChannelsDeclaringTheSameSlot_KeepSeparateLiveRows()
    {
        Product product = BuildProduct();
        ChannelProduct firstLink = BuildChannelProduct(product, ChannelId);
        ChannelProduct secondLink = BuildChannelProduct(product, OtherChannelId);

        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductPhotoService service = BuildService(storage, ValidWebP, OtherWebP);

        MarketplacePhotoSource[] sources = { BuildSource("1_1_thumbnail", 1, "thumbnail") };

        ProductPhotoImportResult first = await service.SyncChannelPhotosAsync(
            firstLink, sources, CancellationToken.None);

        ProductPhotoImportResult second = await service.SyncChannelPhotosAsync(
            secondLink, sources, CancellationToken.None);

        first.Imported.Should().Be(1);
        second.Imported.Should().Be(1, "each card owns its own slot");

        firstLink.Photos.Should().ContainSingle(p => p.Status == ProductPhotoStatus.Uploaded);
        secondLink.Photos.Should().ContainSingle(p => p.Status == ProductPhotoStatus.Uploaded);
        product.Photos.Should().BeEmpty("a channel does not write the canonical gallery");

        firstLink.Photos.Select(p => p.StorageKey)
            .Should().NotIntersectWith(secondLink.Photos.Select(p => p.StorageKey));
    }

    [Fact]
    public async Task SyncChannelPhotosAsync_TwoChannelsWithIdenticalBytes_ShareOneStorageKey()
    {
        Product product = BuildProduct();
        ChannelProduct firstLink = BuildChannelProduct(product, ChannelId);
        ChannelProduct secondLink = BuildChannelProduct(product, OtherChannelId);

        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductPhotoService service = BuildService(storage, ValidWebP, ValidWebP);

        MarketplacePhotoSource[] sources = { BuildSource("1_1_thumbnail", 1, "thumbnail") };

        await service.SyncChannelPhotosAsync(firstLink, sources, CancellationToken.None);
        await service.SyncChannelPhotosAsync(secondLink, sources, CancellationToken.None);

        string firstKey = firstLink.Photos.Single().StorageKey;
        string secondKey = secondLink.Photos.Single().StorageKey;

        firstKey.Should().Be(secondKey);

        // Uploaded once per run, both times to the same content-addressed key: the second channel
        // costs no extra object, which is what content addressing buys across channels.
        storage.Verify(
            s => s.UploadAsync(
                StorageArea.ProductPhotos,
                It.Is<string>(key => key == firstKey),
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    // ---------- ImportCanonicalPhotosAsync: failures ----------

    [Fact]
    public async Task ImportCanonicalPhotosAsync_WhenDownloadFails_KeepsTheGoodPhotoUploaded()
    {
        Product product = BuildProduct();

        ProductPhoto existing = BuildUploadedPhoto(
            product,
            "1_1_thumbnail",
            Sha256Hex(ValidWebP),
            "product-photos/good.webp");

        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductPhotoService service = BuildServiceWithStatus(storage, HttpStatusCode.NotFound);

        ProductPhotoImportResult result = await service.ImportCanonicalPhotosAsync(
            product,
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
    public async Task ImportCanonicalPhotosAsync_WhenFirstDownloadFails_CreatesAFailedRow()
    {
        Product product = BuildProduct();
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();

        ProductPhotoService service = BuildServiceWithStatus(storage, HttpStatusCode.NotFound);

        ProductPhotoImportResult result = await service.ImportCanonicalPhotosAsync(
            product,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        result.Failed.Should().Be(1);

        ProductPhoto photo = product.Photos.Should().ContainSingle().Subject;

        photo.Status.Should().Be(ProductPhotoStatus.Failed);
        photo.ErrorMessage.Should().NotBeNull();
        photo.ContentHash.Should().BeNull();

        VerifyNoUpload(storage);
    }

    [Fact]
    public async Task ImportCanonicalPhotosAsync_WhenUploadFails_KeepsThePriorPhotoAndCreatesNoOrphan()
    {
        Product product = BuildProduct();

        ProductPhoto existing = BuildUploadedPhoto(
            product,
            "1_1_thumbnail",
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

        ProductPhotoImportResult result = await service.ImportCanonicalPhotosAsync(
            product,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        result.Failed.Should().Be(1);
        result.Replaced.Should().Be(0);

        product.Photos.Should().ContainSingle("a failed replacement must not add a row");
        existing.Status.Should().Be(ProductPhotoStatus.Uploaded);
        existing.ErrorMessage.Should().NotBeNull();
    }

    [Fact]
    public async Task ImportCanonicalPhotosAsync_WhenUploadThrows_CountsFailureAndContinuesWithRemainingSources()
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

        ProductPhotoImportResult result = await service.ImportCanonicalPhotosAsync(product, sources, CancellationToken.None);

        result.Failed.Should().Be(2);
        result.Imported.Should().Be(0);
        result.Skipped.Should().Be(0);

        product.Photos.Should().HaveCount(2);
        product.Photos.Should().OnlyContain(p => p.Status == ProductPhotoStatus.Failed);
        product.Photos.Should().OnlyContain(p => p.ErrorMessage != null);
    }

    [Fact]
    public async Task ImportCanonicalPhotosAsync_WhenOneSourceFails_StillImportsTheOthers()
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

        ProductPhotoImportResult result = await service.ImportCanonicalPhotosAsync(product, sources, CancellationToken.None);

        result.Failed.Should().Be(1);
        result.Imported.Should().Be(1);
        product.Photos.Should().ContainSingle(p => p.Status == ProductPhotoStatus.Uploaded);
        product.Photos.Should().ContainSingle(p => p.Status == ProductPhotoStatus.Failed);
    }

    // ---------- ImportCanonicalPhotosAsync: duplicates, tenant, validation ----------

    [Fact]
    public async Task ImportCanonicalPhotosAsync_SkipsDuplicateExternalIdWithinOneBatch()
    {
        Product product = BuildProduct();
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();

        ProductPhotoService service = BuildService(storage, ValidWebP);

        MarketplacePhotoSource[] sources =
        {
            BuildSource("1_1_thumbnail", 1, "thumbnail"),
            BuildSource("1_1_thumbnail", 1, "thumbnail")
        };

        ProductPhotoImportResult result = await service.ImportCanonicalPhotosAsync(product, sources, CancellationToken.None);

        result.Imported.Should().Be(1);
        result.Skipped.Should().Be(1);
        product.Photos.Should().HaveCount(1);
    }

    [Fact]
    public async Task ImportCanonicalPhotosAsync_WithoutTenant_Throws()
    {
        Product product = BuildProduct();
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();

        ProductPhotoService service = BuildServiceWith(storage, null, HttpStatusCode.OK, ValidWebP);

        Func<Task> act = async () => await service.ImportCanonicalPhotosAsync(
            product,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ImportCanonicalPhotosAsync_WithInvalidWebP_FailsWithoutUploading()
    {
        Product product = BuildProduct();
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();

        byte[] notWebP = new byte[64];
        notWebP[0] = 0x47;

        ProductPhotoService service = BuildService(storage, notWebP);

        ProductPhotoImportResult result = await service.ImportCanonicalPhotosAsync(
            product,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        result.Failed.Should().Be(1);
        VerifyNoUpload(storage);
    }

    [Fact]
    public async Task ImportCanonicalPhotosAsync_WithOversizedPayload_FailsWithoutUploading()
    {
        Product product = BuildProduct();
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();

        // Just over the 10 MiB limit, with an otherwise valid WebP header.
        byte[] tooLarge = BuildWebP(10 * 1024 * 1024 + 1);

        ProductPhotoService service = BuildService(storage, tooLarge);

        ProductPhotoImportResult result = await service.ImportCanonicalPhotosAsync(
            product,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        result.Failed.Should().Be(1);
        VerifyNoUpload(storage);
    }

    [Fact]
    public async Task ImportCanonicalPhotosAsync_WhenDownloadReturnsErrorStatus_FailsWithoutUploading()
    {
        Product product = BuildProduct();
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();

        ProductPhotoService service = BuildServiceWith(storage, TenantId, HttpStatusCode.NotFound, ValidWebP);

        ProductPhotoImportResult result = await service.ImportCanonicalPhotosAsync(
            product,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        result.Failed.Should().Be(1);
        product.Photos.Should().ContainSingle(p => p.ErrorMessage != null);
    }

    [Fact]
    public async Task ImportCanonicalPhotosAsync_AddsRowsWithStorageMetadata()
    {
        Product product = BuildProduct();
        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();

        ProductPhotoService service = BuildService(storage, ValidWebP);

        await service.ImportCanonicalPhotosAsync(
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

        await service.DeletePhotosAsync(
            new[] { "product-photos/a.webp", "product-photos/b.webp" },
            CancellationToken.None);

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

        await service.DeletePhotosAsync(Array.Empty<string>(), CancellationToken.None);

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

        string url = service.GetPublicUrl("product-photos/a.webp");

        url.Should().Be("https://cdn.example.com/product-photos/a.webp");
    }

    // ---------- ImportCanonicalPhotosAsync: rendered size ----------

    [Fact]
    public async Task ImportCanonicalPhotosAsync_StoresTheRenderedSizeFromTheWebPHeader()
    {
        // A landscape source rendered into a 3:4 box: the declared 516x688 is a box the image
        // never fills, so accepting it would reserve the wrong space in the layout.
        byte[] landscape = BuildLandscapeWebP(328, 255);

        Product product = BuildProduct();

        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductPhotoService service = BuildService(storage, landscape);

        await service.ImportCanonicalPhotosAsync(
            product,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        ProductPhoto photo = product.Photos.Should().ContainSingle().Subject;

        photo.Width.Should().Be(328);
        photo.Height.Should().Be(255);
        photo.Status.Should().Be(ProductPhotoStatus.Uploaded);
    }

    [Fact]
    public async Task ImportCanonicalPhotosAsync_WhenTheHeaderIsNotUnderstood_KeepsTheDeclaredSize()
    {
        // The synthetic payload has a valid RIFF/WEBP signature but no readable chunk.
        Product product = BuildProduct();

        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductPhotoService service = BuildService(storage, ValidWebP);

        await service.ImportCanonicalPhotosAsync(
            product,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        ProductPhoto photo = product.Photos.Should().ContainSingle().Subject;

        photo.Width.Should().Be(516, "the declared size stays as the fallback");
        photo.Height.Should().Be(688);
    }

    [Fact]
    public async Task ImportCanonicalPhotosAsync_WhenDownloadFails_RecordsTheDeclaredSizeOnTheFailedRow()
    {
        Product product = BuildProduct();

        Mock<IObjectStorage> storage = new Mock<IObjectStorage>();
        ProductPhotoService service = BuildServiceWithStatus(storage, HttpStatusCode.NotFound);

        await service.ImportCanonicalPhotosAsync(
            product,
            new[] { BuildSource("1_1_thumbnail", 1, "thumbnail") },
            CancellationToken.None);

        ProductPhoto photo = product.Photos.Should().ContainSingle().Subject;

        photo.Status.Should().Be(ProductPhotoStatus.Failed);
        photo.Width.Should().Be(516, "a row that never held bytes still knows the declared size");
        photo.Height.Should().Be(688);
        photo.ErrorMessage.Should().NotBeNull();
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
        string? contentHash,
        string storageKey)
    {
        ProductPhoto photo = new ProductPhoto
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
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

    private static ChannelProduct BuildChannelProduct(Product product, Guid? channelId = null)
    {
        return new ChannelProduct
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ChannelId = channelId ?? ChannelId,
            ExternalProductId = Guid.NewGuid().ToString("N"),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
    }

    // Added to channelProduct.Photos by the builder, for the same reason as BuildUploadedPhoto.
    private static ChannelProductPhoto BuildUploadedChannelPhoto(
        ChannelProduct channelProduct,
        string externalId,
        string? contentHash,
        string storageKey)
    {
        ChannelProductPhoto photo = new ChannelProductPhoto
        {
            Id = Guid.NewGuid(),
            ChannelProductId = channelProduct.Id,
            ExternalId = externalId,
            SizeVariant = "thumbnail",
            DisplayOrder = 1,
            ContentHash = contentHash,
            StorageKey = storageKey,
            ContentType = "image/webp",
            SizeBytes = 64,
            Source = "Wildberries",
            Status = ProductPhotoStatus.Uploaded,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };

        channelProduct.Photos.Add(photo);

        return photo;
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

    /// <summary>
    /// A WebP whose VP8X chunk declares the given canvas, so the service can read a size that
    /// differs from the one the marketplace declared.
    /// </summary>
    private static byte[] BuildLandscapeWebP(int width, int height)
    {
        byte[] payload = BuildWebP(64);

        payload[12] = (byte)'V';
        payload[13] = (byte)'P';
        payload[14] = (byte)'8';
        payload[15] = (byte)'X';

        int storedWidth = width - 1;
        int storedHeight = height - 1;

        payload[24] = (byte)(storedWidth & 0xFF);
        payload[25] = (byte)((storedWidth >> 8) & 0xFF);
        payload[26] = (byte)((storedWidth >> 16) & 0xFF);
        payload[27] = (byte)(storedHeight & 0xFF);
        payload[28] = (byte)((storedHeight >> 8) & 0xFF);
        payload[29] = (byte)((storedHeight >> 16) & 0xFF);

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
