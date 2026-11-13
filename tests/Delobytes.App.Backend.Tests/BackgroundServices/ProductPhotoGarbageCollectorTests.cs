using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Options;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Catalog.Infrastructure.BackgroundServices;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Contracts.Storage;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.BackgroundServices;

/// <summary>
/// Tests <see cref="ProductPhotoGarbageCollector"/>: only <c>Removed</c> rows past the retention
/// period may be deleted, a blob shared with a live row must survive, and one failing tenant must
/// not stall the sweep of the others.
/// </summary>
[Collection("BackgroundServices")]
public class ProductPhotoGarbageCollectorTests : IDisposable
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ChannelId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly string _databaseName;
    private readonly MessageTenantContext _tenantContext;
    private readonly Mock<IObjectStorage> _storageMock;
    private readonly Mock<ITenantRepository> _tenantRepositoryMock;
    private readonly ServiceProvider _provider;
    private readonly ProductPhotoGarbageCollector _collector;

    public ProductPhotoGarbageCollectorTests()
    {
        _databaseName = "photo-gc-" + Guid.NewGuid().ToString("N");
        _tenantContext = new MessageTenantContext();
        _storageMock = new Mock<IObjectStorage>();
        _tenantRepositoryMock = new Mock<ITenantRepository>();

        _tenantRepositoryMock
            .Setup(r => r.GetAllActiveTenantIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { TenantId });

        ServiceCollection services = new ServiceCollection();

        services.AddSingleton<ITenantContext>(_tenantContext);
        services.AddSingleton<ITenantContextSetter>(_tenantContext);
        services.AddSingleton(_tenantContext);
        services.AddSingleton(_storageMock.Object);
        services.AddScoped(_ => _tenantRepositoryMock.Object);
        services.AddSingleton<DbContextOptions<CatalogDbContext>>(
            new DbContextOptionsBuilder<CatalogDbContext>()
                .UseInMemoryDatabase(_databaseName)
                .Options);
        services.AddScoped(provider => new CatalogDbContext(
            provider.GetRequiredService<DbContextOptions<CatalogDbContext>>(),
            provider.GetRequiredService<ITenantContext>()));

        _provider = services.BuildServiceProvider();

        _collector = new ProductPhotoGarbageCollector(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new ProductPhotoRetentionOptions { RetentionDays = 30, IntervalHours = 24 }),
            NullLogger<ProductPhotoGarbageCollector>.Instance,
            TimeProvider.System);
    }

    public void Dispose()
    {
        _provider.Dispose();
    }

    [Fact]
    public async Task Cleanup_RemovedRowPastRetention_IsDeletedWithItsBlob()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await SeedPhotoAsync(TenantId, ProductPhotoStatus.Removed, "product-photos/expired.webp", now.AddDays(-40));

        ProductPhotoGcResult result = await _collector.CleanupAsync(now, CancellationToken.None);

        result.DeletedPhotos.Should().Be(1);
        result.DeletedBlobs.Should().Be(1);
        result.FailedTenants.Should().Be(0);

        _storageMock.Verify(
            s => s.DeleteManyAsync(
                StorageArea.ProductPhotos,
                It.Is<IReadOnlyCollection<string>>(keys => keys.Count == 1 && keys.Contains("product-photos/expired.webp")),
                It.IsAny<CancellationToken>()),
            Times.Once);

        List<ProductPhoto> remaining = await ReadAllPhotosAsync();
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task Cleanup_RemovedRowWithinRetention_IsKept()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await SeedPhotoAsync(TenantId, ProductPhotoStatus.Removed, "product-photos/recent.webp", now.AddDays(-5));

        ProductPhotoGcResult result = await _collector.CleanupAsync(now, CancellationToken.None);

        result.DeletedPhotos.Should().Be(0);
        result.DeletedBlobs.Should().Be(0);

        VerifyNoBlobDeleted();

        List<ProductPhoto> remaining = await ReadAllPhotosAsync();
        remaining.Should().ContainSingle()
            .Which.Status.Should().Be(ProductPhotoStatus.Removed);
    }

    [Fact]
    public async Task Cleanup_RemovedRowWithoutRemovedAt_IsNeverTouched()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await SeedPhotoAsync(TenantId, ProductPhotoStatus.Removed, "product-photos/undated.webp", null);

        ProductPhotoGcResult result = await _collector.CleanupAsync(now, CancellationToken.None);

        result.DeletedPhotos.Should().Be(0);
        VerifyNoBlobDeleted();

        (await ReadAllPhotosAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Cleanup_LiveRowsAreNeverDeletedOrCollected()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await SeedPhotoAsync(TenantId, ProductPhotoStatus.Uploaded, "product-photos/live.webp", null);
        await SeedPhotoAsync(TenantId, ProductPhotoStatus.Failed, "product-photos/failed.webp", null);
        await SeedPhotoAsync(TenantId, ProductPhotoStatus.Pending, "product-photos/pending.webp", null);

        ProductPhotoGcResult result = await _collector.CleanupAsync(now, CancellationToken.None);

        result.DeletedPhotos.Should().Be(0);
        VerifyNoBlobDeleted();

        List<ProductPhoto> remaining = await ReadAllPhotosAsync();
        remaining.Should().HaveCount(3);
        remaining.Should().NotContain(p => p.Status == ProductPhotoStatus.Removed);
    }

    [Fact]
    public async Task Cleanup_ExpiredRowWhoseBlobIsStillReferencedByLiveRow_DeletesTheRowButKeepsTheBlob()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string sharedKey = "product-photos/shared.webp";

        await SeedPhotoAsync(TenantId, ProductPhotoStatus.Uploaded, sharedKey, null);
        await SeedPhotoAsync(TenantId, ProductPhotoStatus.Removed, sharedKey, now.AddDays(-60));

        ProductPhotoGcResult result = await _collector.CleanupAsync(now, CancellationToken.None);

        result.DeletedPhotos.Should().Be(1);
        result.DeletedBlobs.Should().Be(0);

        VerifyNoBlobDeleted();

        List<ProductPhoto> remaining = await ReadAllPhotosAsync();
        remaining.Should().ContainSingle();
        remaining.Single().Status.Should().Be(ProductPhotoStatus.Uploaded);
        remaining.Single().StorageKey.Should().Be(sharedKey);
    }

    [Fact]
    public async Task Cleanup_TwoExpiredRowsSharingOneBlob_DeleteTheBlobOnce()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string sharedKey = "product-photos/shared-by-two-removed.webp";

        await SeedPhotoAsync(TenantId, ProductPhotoStatus.Removed, sharedKey, now.AddDays(-45));
        await SeedPhotoAsync(TenantId, ProductPhotoStatus.Removed, sharedKey, now.AddDays(-45));

        ProductPhotoGcResult result = await _collector.CleanupAsync(now, CancellationToken.None);

        result.DeletedPhotos.Should().Be(2);
        result.DeletedBlobs.Should().Be(1);

        _storageMock.Verify(
            s => s.DeleteManyAsync(
                StorageArea.ProductPhotos,
                It.Is<IReadOnlyCollection<string>>(keys => keys.Count == 1 && keys.Contains(sharedKey)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Cleanup_FailingTenant_IsCountedAndDoesNotStopTheOthers()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        _tenantRepositoryMock
            .Setup(r => r.GetAllActiveTenantIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { TenantId, OtherTenantId });

        _storageMock
            .Setup(s => s.DeleteManyAsync(
                It.IsAny<StorageArea>(),
                It.Is<IReadOnlyCollection<string>>(keys => keys.Contains("product-photos/broken.webp")),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ObjectStorageException("bucket unavailable"));

        await SeedPhotoAsync(TenantId, ProductPhotoStatus.Removed, "product-photos/broken.webp", now.AddDays(-40));
        await SeedPhotoAsync(OtherTenantId, ProductPhotoStatus.Removed, "product-photos/healthy.webp", now.AddDays(-40));

        ProductPhotoGcResult result = await _collector.CleanupAsync(now, CancellationToken.None);

        result.FailedTenants.Should().Be(1);
        result.DeletedPhotos.Should().Be(1);
        result.DeletedBlobs.Should().Be(1);

        List<ProductPhoto> remaining = await ReadAllPhotosAsync();
        remaining.Should().ContainSingle()
            .Which.StorageKey.Should().Be("product-photos/broken.webp");
    }

    [Fact]
    public async Task Cleanup_WithNoActiveTenants_DoesNothing()
    {
        _tenantRepositoryMock
            .Setup(r => r.GetAllActiveTenantIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        DateTimeOffset now = DateTimeOffset.UtcNow;
        await SeedPhotoAsync(TenantId, ProductPhotoStatus.Removed, "product-photos/expired.webp", now.AddDays(-40));

        ProductPhotoGcResult result = await _collector.CleanupAsync(now, CancellationToken.None);

        result.DeletedPhotos.Should().Be(0);
        result.DeletedBlobs.Should().Be(0);
        result.FailedTenants.Should().Be(0);

        VerifyNoBlobDeleted();
        (await ReadAllPhotosAsync()).Should().ContainSingle();
    }

    private void VerifyNoBlobDeleted()
    {
        _storageMock.Verify(
            s => s.DeleteManyAsync(
                It.IsAny<StorageArea>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private async Task SeedPhotoAsync(
        Guid tenantId,
        ProductPhotoStatus status,
        string storageKey,
        DateTimeOffset? removedAt)
    {
        using IServiceScope scope = _provider.CreateScope();

        _tenantContext.SetTenantId(tenantId);

        try
        {
            CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

            Product product = new Product
            {
                Id = Guid.NewGuid(),
                Code = "C-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Sku = "SKU-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Name = "Test product",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-100)
            };

            product.Photos.Add(new ProductPhoto
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                ChannelId = ChannelId,
                DisplayOrder = 1,
                SizeVariant = "thumbnail",
                StorageKey = storageKey,
                ContentHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                OriginalFileName = "photo.webp",
                ContentType = "image/webp",
                SizeBytes = 64,
                Source = "Wildberries",
                ExternalId = "1_1_thumbnail",
                Status = status,
                RemovedAt = removedAt,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-100)
            });

            await context.Products.AddAsync(product);
            await context.SaveChangesAsync();
        }
        finally
        {
            _tenantContext.Clear();
        }
    }

    private async Task<List<ProductPhoto>> ReadAllPhotosAsync()
    {
        using IServiceScope scope = _provider.CreateScope();

        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        return await context.ProductPhotos
            .IgnoreQueryFilters()
            .ToListAsync();
    }
}
