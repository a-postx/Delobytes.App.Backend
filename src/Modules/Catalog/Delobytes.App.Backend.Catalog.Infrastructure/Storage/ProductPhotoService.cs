using System.Net.Http;
using System.Security.Cryptography;
using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Contracts.Storage;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Storage;

/// <summary>
/// High-level photo pipeline: downloads a marketplace photo, verifies it is WebP and
/// hands the bytes to <see cref="IObjectStorage"/>. Owns the content-addressed key convention,
/// the size limit and the reconciliation rules; knows nothing about HTTP retries or transactions.
/// </summary>
public class ProductPhotoService : IProductPhotoService
{
    /// <summary>Named <see cref="HttpClient"/> used for marketplace downloads; see module DI registration.</summary>
    public const string HttpClientName = nameof(ProductPhotoService);

    /// <summary>Upper bound for a single photo; larger payloads are rejected before upload.</summary>
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Keys are derived from the content hash, so the bytes behind a URL never change.
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
    private static ProductPhoto CreatePendingPhoto(
        Product product,
        MarketplacePhotoSource source,
        Guid channelId)
    {
        ProductPhoto photo = new ProductPhoto
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ChannelId = channelId,
            DisplayOrder = source.DisplayOrder,
            SizeVariant = source.SizeVariant,
            Source = source.Source,
            ExternalId = source.ExternalId,
            ContentType = WebPContentType,
            OriginalFileName = string.Empty,
            StorageKey = string.Empty,
            Status = ProductPhotoStatus.Pending,

            // AuditableEntityInterceptor fills CreatedAt on save, but callers may read the
            // entity before SaveChanges is ever called, so the value is set here as well.
            CreatedAt = DateTimeOffset.UtcNow
        };

        product.Photos.Add(photo);

        return photo;
    }

    private static ProductPhoto CreateUploadedPhoto(
        Product product,
        MarketplacePhotoSource source,
        Guid channelId,
        string contentHash,
        string storageKey,
        byte[] payload,
        DateTimeOffset now)
    {
        Guid photoId = Guid.NewGuid();

        ProductPhoto photo = new ProductPhoto
        {
            Id = photoId,
            ProductId = product.Id,
            ChannelId = channelId,
            DisplayOrder = source.DisplayOrder,
            SizeVariant = source.SizeVariant,
            Source = source.Source,
            ExternalId = source.ExternalId,
            ContentHash = contentHash,
            StorageKey = storageKey,
            ContentType = WebPContentType,
            OriginalFileName = $"{photoId:N}.{ProductPhotoKeys.Extension}",
            SizeBytes = payload.Length,
            Width = source.Width,
            Height = source.Height,
            Status = ProductPhotoStatus.Uploaded,
            LastCheckedAt = now,
            CreatedAt = now
        };

        product.Photos.Add(photo);

        return photo;
    }

    private static void MarkFailed(ProductPhoto photo, string errorMessage, DateTimeOffset now)
    {
        photo.Status = ProductPhotoStatus.Failed;
        photo.ErrorMessage = Truncate(errorMessage, 1000);
        photo.LastCheckedAt = now;
        photo.UpdatedAt = now;
    }

    // A failed download or upload must not degrade a photo that is already served: the CDN can be
    // unreachable while the stored bytes are still correct. The error is recorded for visibility only.
    private static void RecordCheckFailure(ProductPhoto photo, string errorMessage, DateTimeOffset now)
    {
        photo.ErrorMessage = Truncate(errorMessage, 1000);
        photo.LastCheckedAt = now;
        photo.UpdatedAt = now;
    }

    // The live row of a slot in this channel. Guid.Empty rows are legacy rows awaiting adoption by
    // whichever channel imports them next; that is why they participate here.
    private ProductPhoto? FindLivePhoto(Product product, Guid channelId, string externalId)
    {
        List<ProductPhoto> matches = product.Photos
            .Where(p => p.Status != ProductPhotoStatus.Removed
                        && string.Equals(p.ExternalId, externalId, StringComparison.Ordinal)
                        && (p.ChannelId == channelId || p.ChannelId == Guid.Empty))
            .OrderByDescending(p => p.CreatedAt)
            .ToList();

        if (matches.Count > 1)
        {
            _logger.LogWarning(
                "Product {ProductId} has {Count} live photo rows for ExternalId {ExternalId} in ChannelId {ChannelId}; using the newest.",
                product.Id,
                matches.Count,
                externalId,
                channelId);
        }

        return matches.Count == 0 ? null : matches[0];
    }

    /// <inheritdoc/>
    public async Task<ProductPhotoImportResult> ImportPhotosAsync(
        Product product,
        Guid channelId,
        IReadOnlyList<MarketplacePhotoSource> photos,
        CancellationToken cancellationToken)
    {
        ProductPhotoImportResult result = new ProductPhotoImportResult();

        // The tenant is taken from the execution context rather than from a parameter:
        // a caller cannot then write photos into a tenant it does not belong to.
        Guid tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Cannot import photos without a resolved TenantId.");

        // A duplicate ExternalId inside one message is processed once. Reconciliation below still
        // runs for an empty list, because "the card no longer declares any photo" is the signal
        // that every live photo of this channel must be removed.
        HashSet<string> handledInThisRun = new HashSet<string>(StringComparer.Ordinal);

        HttpClient httpClient = _httpClientFactory.CreateClient(HttpClientName);

        foreach (MarketplacePhotoSource source in photos)
        {
            if (!handledInThisRun.Add(source.ExternalId))
            {
                result.Skipped++;
                continue;
            }

            try
            {
                PhotoImportOutcome outcome = await ReconcilePhotoAsync(
                    product,
                    channelId,
                    source,
                    tenantId,
                    httpClient,
                    cancellationToken);

                switch (outcome)
                {
                    case PhotoImportOutcome.Imported:
                        result.Imported++;
                        break;
                    case PhotoImportOutcome.Replaced:
                        result.Replaced++;
                        break;
                    case PhotoImportOutcome.Skipped:
                        result.Skipped++;
                        break;
                    case PhotoImportOutcome.Failed:
                        result.Failed++;
                        break;
                    default:
                        throw new InvalidOperationException($"Unhandled photo import outcome {outcome}.");
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

        RemoveUndeclaredPhotos(product, channelId, photos, result);

        return result;
    }

    // Marks every live row of this channel that the card no longer declares. Rows of other channels
    // are never touched, and neither are Guid.Empty rows: no channel can declare them absent, their
    // own next import adopts them.
    private void RemoveUndeclaredPhotos(
        Product product,
        Guid channelId,
        IReadOnlyList<MarketplacePhotoSource> photos,
        ProductPhotoImportResult result)
    {
        HashSet<string> declaredExternalIds = photos
            .Select(s => s.ExternalId)
            .ToHashSet(StringComparer.Ordinal);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        List<ProductPhoto> undeclared = product.Photos
            .Where(p => p.Status == ProductPhotoStatus.Uploaded
                        && p.ChannelId == channelId
                        && !declaredExternalIds.Contains(p.ExternalId))
            .ToList();

        foreach (ProductPhoto photo in undeclared)
        {
            photo.Status = ProductPhotoStatus.Removed;
            photo.RemovedAt = now;
            photo.UpdatedAt = now;
            result.Removed++;
        }

        if (undeclared.Count == 0)
        {
            return;
        }

        bool anyPhotoLeft = product.Photos.Any(p => p.Status == ProductPhotoStatus.Uploaded);

        if (!anyPhotoLeft)
        {
            _logger.LogWarning(
                "Photo set of ChannelId {ChannelId} no longer declares any of the {Removed} photo(s) of Product {ProductId}, which now has no uploaded photos at all.",
                channelId,
                undeclared.Count,
                product.Id);
        }
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

    // Reconciles one declared slot of one channel's card. The bytes are always downloaded: the
    // marketplace keeps the URL when the image in a slot is replaced and exposes no digest, so the
    // hash of the payload is the only reliable change detector.
    private async Task<PhotoImportOutcome> ReconcilePhotoAsync(
        Product product,
        Guid channelId,
        MarketplacePhotoSource source,
        Guid tenantId,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        ProductPhoto? livePhoto = FindLivePhoto(product, channelId, source.ExternalId);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        (byte[]? payload, string? failureReason) = await DownloadAsync(
            httpClient,
            source,
            product.Id,
            cancellationToken);

        if (payload is null)
        {
            string reason = failureReason ?? "Photo download failed.";

            if (livePhoto is null)
            {
                ProductPhoto pendingPhoto = CreatePendingPhoto(product, source, channelId);
                MarkFailed(pendingPhoto, reason, now);

                return PhotoImportOutcome.Failed;
            }

            if (livePhoto.Status == ProductPhotoStatus.Uploaded)
            {
                RecordCheckFailure(livePhoto, reason, now);

                return PhotoImportOutcome.Failed;
            }

            MarkFailed(livePhoto, reason, now);

            return PhotoImportOutcome.Failed;
        }

        string contentHash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

        if (livePhoto is not null && livePhoto.Status == ProductPhotoStatus.Uploaded)
        {
            if (string.Equals(livePhoto.ContentHash, contentHash, StringComparison.Ordinal))
            {
                livePhoto.LastCheckedAt = now;

                return PhotoImportOutcome.Skipped;
            }

            if (livePhoto.ContentHash is null)
            {
                // Adoption: the object behind StorageKey is by construction the bytes just hashed,
                // so key, size and dimensions stay as persisted.
                livePhoto.ContentHash = contentHash;

                if (livePhoto.ChannelId == Guid.Empty)
                {
                    livePhoto.ChannelId = channelId;
                }

                livePhoto.LastCheckedAt = now;
                livePhoto.UpdatedAt = now;

                return PhotoImportOutcome.Skipped;
            }
        }

        string storageKey = ProductPhotoKeys.Build(tenantId, product.Id, contentHash);

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

            string reason = "Upload to object storage failed: " + ex.Message;

            if (livePhoto is null)
            {
                ProductPhoto pendingPhoto = CreatePendingPhoto(product, source, channelId);
                MarkFailed(pendingPhoto, reason, now);
            }
            else if (livePhoto.Status == ProductPhotoStatus.Uploaded)
            {
                RecordCheckFailure(livePhoto, reason, now);
            }
            else
            {
                MarkFailed(livePhoto, reason, now);
            }

            return PhotoImportOutcome.Failed;
        }

        CreateUploadedPhoto(product, source, channelId, contentHash, storageKey, payload, now);

        if (livePhoto is null)
        {
            return PhotoImportOutcome.Imported;
        }

        // A superseded row that never held a blob is dropped instead of retained: the grace period
        // exists to keep a served object alive, and this row never had one.
        if (string.IsNullOrEmpty(livePhoto.StorageKey))
        {
            product.Photos.Remove(livePhoto);
        }
        else
        {
            livePhoto.Status = ProductPhotoStatus.Removed;
            livePhoto.RemovedAt = now;
            livePhoto.UpdatedAt = now;
        }

        return PhotoImportOutcome.Replaced;
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

    private enum PhotoImportOutcome
    {
        Imported,
        Replaced,
        Skipped,
        Failed
    }
}
