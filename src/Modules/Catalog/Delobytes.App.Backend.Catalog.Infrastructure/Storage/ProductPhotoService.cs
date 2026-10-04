using System.Net.Http;
using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Contracts.Storage;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Storage;

/// <summary>
/// High-level photo pipeline: downloads a marketplace photo, verifies it is WebP and
/// hands the bytes to <see cref="IObjectStorage"/>. Owns the key convention, the size
/// limit and the idempotency rules; knows nothing about HTTP retries or transactions.
/// </summary>
public class ProductPhotoService : IProductPhotoService
{
    /// <summary>Named <see cref="HttpClient"/> used for marketplace downloads; see module DI registration.</summary>
    public const string HttpClientName = nameof(ProductPhotoService);

    /// <summary>Upper bound for a single photo; larger payloads are rejected before upload.</summary>
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Photos are content-addressed by a generated GUID, so the bytes behind a URL never change.
    /// One year + immutable lets CDNs and browsers skip revalidation entirely.
    /// </summary>
    private const string CacheControl = "public, max-age=31536000, immutable";

    private const string WebPContentType = "image/webp";

    private readonly IObjectStorage _objectStorage;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<ProductPhotoService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductPhotoService"/> class.
    /// </summary>
    /// <param name="objectStorage">Low-level blob storage.</param>
    /// <param name="httpClientFactory">Factory for the named download client.</param>
    /// <param name="tenantContext">Source of the current tenant; photos are keyed by it.</param>
    /// <param name="logger">Logger.</param>
    public ProductPhotoService(
        IObjectStorage objectStorage,
        IHttpClientFactory httpClientFactory,
        ITenantContext tenantContext,
        ILogger<ProductPhotoService> logger)
    {
        _objectStorage = objectStorage;
        _httpClientFactory = httpClientFactory;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    // Stops reading one byte past the limit, so an unexpectedly huge or endless response
    // cannot exhaust memory before it is rejected.
    private static async Task CopyWithLimitAsync(
        Stream source,
        Stream destination,
        long limitBytes,
        CancellationToken cancellationToken)
    {
        byte[] chunk = new byte[81920];
        long total = 0;

        while (true)
        {
            int read = await source.ReadAsync(chunk, cancellationToken);

            if (read <= 0)
            {
                break;
            }

            total += read;

            if (total > limitBytes)
            {
                return;
            }

            await destination.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    // Every row starts as Pending and is moved to Uploaded or Failed by the caller,
    // so the row lifecycle has exactly one entry point.
    private static ProductPhoto CreatePhoto(Product product, MarketplacePhotoSource source, Guid tenantId)
    {
        ProductPhoto photo = new ProductPhoto
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            DisplayOrder = source.DisplayOrder,
            SizeVariant = source.SizeVariant,
            Source = source.Source,
            ExternalId = source.ExternalId,
            ContentType = WebPContentType,
            Status = ProductPhotoStatus.Pending,

            // AuditableEntityInterceptor fills CreatedAt on save, but callers may read the
            // entity before SaveChanges is ever called, so the value is set here as well.
            CreatedAt = DateTimeOffset.UtcNow
        };

        photo.StorageKey = ProductPhotoKeys.Build(tenantId, product.Id, photo.Id);

        product.Photos.Add(photo);

        return photo;
    }

    private static void MarkFailed(ProductPhoto photo, string errorMessage)
    {
        photo.Status = ProductPhotoStatus.Failed;
        photo.ErrorMessage = Truncate(errorMessage, 1000);
        photo.UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <inheritdoc/>
    public async Task<ProductPhotoImportResult> ImportPhotosAsync(
        Product product,
        IReadOnlyList<MarketplacePhotoSource> photos,
        CancellationToken cancellationToken)
    {
        ProductPhotoImportResult result = new ProductPhotoImportResult();

        if (photos.Count == 0)
        {
            return result;
        }

        // The tenant is taken from the execution context rather than from a parameter:
        // a caller cannot then write photos into a tenant it does not belong to.
        Guid tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Cannot import photos without a resolved TenantId.");

        HashSet<string> uploadedExternalIds = product.Photos
            .Where(p => p.Status == ProductPhotoStatus.Uploaded)
            .Select(p => p.ExternalId)
            .ToHashSet(StringComparer.Ordinal);

        // The same ExternalId twice in one batch would otherwise violate the unique index
        // on (TenantId, ProductId, ExternalId) and lose one of the two photos.
        HashSet<string> handledInThisRun = new HashSet<string>(StringComparer.Ordinal);

        HttpClient httpClient = _httpClientFactory.CreateClient(HttpClientName);

        foreach (MarketplacePhotoSource source in photos)
        {
            if (uploadedExternalIds.Contains(source.ExternalId) || !handledInThisRun.Add(source.ExternalId))
            {
                result.Skipped++;
                continue;
            }

            try
            {
                bool imported = await ImportSinglePhotoAsync(
                    product,
                    source,
                    tenantId,
                    httpClient,
                    cancellationToken);

                if (imported)
                {
                    result.Imported++;
                }
                else
                {
                    result.Failed++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One broken photo must not abort the whole product import.
                _logger.LogError(
                    ex,
                    "Unexpected failure while importing photo {ExternalId} for Product {ProductId}",
                    source.ExternalId,
                    product.Id);
                result.Failed++;
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public Task DeletePhotosAsync(IReadOnlyList<ProductPhoto> photos, CancellationToken cancellationToken)
    {
        if (photos.Count == 0)
        {
            return Task.CompletedTask;
        }

        List<string> keys = photos.Select(p => p.StorageKey).ToList();

        return _objectStorage.DeleteManyAsync(StorageArea.ProductPhotos, keys, cancellationToken);
    }

    /// <inheritdoc/>
    public string GetPublicUrl(ProductPhoto photo)
    {
        return _objectStorage.GetPublicUrl(StorageArea.ProductPhotos, photo.StorageKey);
    }

    // Returns true when the photo is stored and represented by a row with Uploaded status.
    // Exactly one row exists per ExternalId: a failed attempt keeps that row (with the reason
    // and the id that fixes the storage key), so the next import retries instead of duplicating.
    private async Task<bool> ImportSinglePhotoAsync(
        Product product,
        MarketplacePhotoSource source,
        Guid tenantId,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        ProductPhoto? existing = product.Photos.FirstOrDefault(
            p => string.Equals(p.ExternalId, source.ExternalId, StringComparison.Ordinal));

        (byte[]? payload, string? failureReason) = await DownloadAsync(
            httpClient,
            source,
            product.Id,
            cancellationToken);

        if (payload is null)
        {
            ProductPhoto failedPhoto = existing ?? CreatePhoto(product, source, tenantId);
            MarkFailed(failedPhoto, failureReason ?? "Photo download failed.");

            return false;
        }

        ProductPhoto photo = existing ?? CreatePhoto(product, source, tenantId);
        string storageKey = ProductPhotoKeys.Build(tenantId, product.Id, photo.Id);

        try
        {
            using MemoryStream content = new MemoryStream(payload, writable: false);

            await _objectStorage.UploadAsync(
                StorageArea.ProductPhotos,
                storageKey,
                content,
                WebPContentType,
                CacheControl,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Upload failed for photo {ExternalId} of Product {ProductId}",
                source.ExternalId,
                product.Id);

            // The row is already created and tracked, so it is marked failed in place.
            // Creating a second row here would collide with the unique index on
            // (TenantId, ProductId, ExternalId) at SaveChanges time.
            MarkFailed(photo, "Upload to object storage failed: " + ex.Message);

            return false;
        }

        photo.StorageKey = storageKey;
        photo.OriginalFileName = $"{photo.Id:N}.{ProductPhotoKeys.Extension}";
        photo.ContentType = WebPContentType;
        photo.SizeBytes = payload.Length;
        photo.Width = source.Width;
        photo.Height = source.Height;
        photo.Status = ProductPhotoStatus.Uploaded;
        photo.ErrorMessage = null;
        photo.UpdatedAt = DateTimeOffset.UtcNow;

        return true;
    }

    /// <summary>
    /// Downloads a photo and validates it. Returns a null payload together with the reason
    /// when the bytes are unusable.
    /// </summary>
    private async Task<(byte[]? Payload, string? FailureReason)> DownloadAsync(
        HttpClient httpClient,
        MarketplacePhotoSource source,
        Guid productId,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            source.Url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Photo download failed for Product {ProductId}, ExternalId {ExternalId}: HTTP {StatusCode}",
                productId,
                source.ExternalId,
                (int)response.StatusCode);
            return (null, $"Download failed with HTTP status {(int)response.StatusCode}.");
        }

        if (response.Content.Headers.ContentLength > MaxFileSizeBytes)
        {
            _logger.LogWarning(
                "Photo exceeds the {LimitBytes} byte limit for Product {ProductId}, ExternalId {ExternalId}",
                MaxFileSizeBytes,
                productId,
                source.ExternalId);
            return (null, $"Photo exceeds the {MaxFileSizeBytes} byte limit.");
        }

        using Stream sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using MemoryStream buffer = new MemoryStream();

        await CopyWithLimitAsync(sourceStream, buffer, MaxFileSizeBytes, cancellationToken);

        if (buffer.Length == 0)
        {
            _logger.LogWarning(
                "Photo download produced no bytes for Product {ProductId}, ExternalId {ExternalId}",
                productId,
                source.ExternalId);
            return (null, "Download produced no bytes.");
        }

        if (buffer.Length > MaxFileSizeBytes)
        {
            _logger.LogWarning(
                "Photo exceeds the {LimitBytes} byte limit for Product {ProductId}, ExternalId {ExternalId}",
                MaxFileSizeBytes,
                productId,
                source.ExternalId);
            return (null, $"Photo exceeds the {MaxFileSizeBytes} byte limit.");
        }

        byte[] payload = buffer.ToArray();

        using MemoryStream signatureSource = new MemoryStream(payload, writable: false);

        if (!WebPValidator.HasValidHeader(signatureSource))
        {
            _logger.LogWarning(
                "Invalid WebP signature for Product {ProductId}, ExternalId {ExternalId}",
                productId,
                source.ExternalId);
            return (null, "Payload is not a valid WebP file.");
        }

        return (payload, null);
    }
}
