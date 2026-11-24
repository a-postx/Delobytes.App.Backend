using System.Net.Http;
using System.Security.Cryptography;
using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Identity;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Contracts.Storage;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Catalog.Application.Services;

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
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductPhotoService"/> class.
    /// </summary>
    /// <param name="objectStorage">Low-level blob storage.</param>
    /// <param name="httpClientFactory">Factory for the named download client.</param>
    /// <param name="tenantContext">Source of the current tenant; photos are keyed by it.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="timeProvider">Clock source.</param>
    public ProductPhotoService(
        IObjectStorage objectStorage,
        IHttpClientFactory httpClientFactory,
        ITenantContext tenantContext,
        ILogger<ProductPhotoService> logger,
        TimeProvider timeProvider)
    {
        _objectStorage = objectStorage;
        _httpClientFactory = httpClientFactory;
        _tenantContext = tenantContext;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public Task<ProductPhotoImportResult> ImportCanonicalPhotosAsync(
        Product product,
        IReadOnlyList<MarketplacePhotoSource> photos,
        CancellationToken cancellationToken)
    {
        PhotoSet rowSet = new PhotoSet
        {
            GetRows = () => product.Photos.Cast<IPhotoRow>().ToList(),
            AddRow = source =>
            {
                ProductPhoto photo = new ProductPhoto
                {
                    // Created here because the upload file name below is derived from it before the
                    // row is ever tracked.
                    Id = EntityId.New(),
                    ProductId = product.Id,
                    ExternalId = source.ExternalId,
                    DisplayOrder = source.DisplayOrder,
                    SizeVariant = source.SizeVariant,
                    Source = source.Source,
                    ContentType = WebPContentType,
                    OriginalFileName = string.Empty,
                    StorageKey = string.Empty,
                    Width = source.Width,
                    Height = source.Height,
                    Status = ProductPhotoStatus.Pending,

                    // AuditableEntityInterceptor fills CreatedAt on save, but callers may read the
                    // entity before SaveChanges is ever called, so the value is set here as well.
                    CreatedAt = _timeProvider.GetUtcNow(),
                };

                product.Photos.Add(photo);

                return photo;
            },
            RemoveRow = row => product.Photos.Remove((ProductPhoto)row),
        };

        return ReconcileAsync(rowSet, product.Id, photos, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<ProductPhotoImportResult> SyncChannelPhotosAsync(
        ChannelProduct channelProduct,
        IReadOnlyList<MarketplacePhotoSource> photos,
        CancellationToken cancellationToken)
    {
        PhotoSet rowSet = new PhotoSet
        {
            GetRows = () => channelProduct.Photos.Cast<IPhotoRow>().ToList(),
            AddRow = source =>
            {
                ChannelProductPhoto photo = new ChannelProductPhoto
                {
                    // Created here because the upload file name below is derived from it before the
                    // row is ever tracked.
                    Id = EntityId.New(),
                    ChannelProductId = channelProduct.Id,
                    ExternalId = source.ExternalId,
                    DisplayOrder = source.DisplayOrder,
                    SizeVariant = source.SizeVariant,
                    Source = source.Source,
                    ContentType = WebPContentType,
                    StorageKey = string.Empty,
                    Width = source.Width,
                    Height = source.Height,
                    Status = ProductPhotoStatus.Pending,
                    CreatedAt = _timeProvider.GetUtcNow(),
                };

                channelProduct.Photos.Add(photo);

                return photo;
            },
            RemoveRow = row => channelProduct.Photos.Remove((ChannelProductPhoto)row),
        };

        return ReconcileAsync(rowSet, channelProduct.ProductId, photos, cancellationToken);
    }

    /// <inheritdoc/>
    public Task DeletePhotosAsync(IReadOnlyList<string> storageKeys, CancellationToken cancellationToken)
    {
        if (storageKeys.Count == 0)
        {
            return Task.CompletedTask;
        }

        return _objectStorage.DeleteManyAsync(StorageArea.ProductPhotos, storageKeys, cancellationToken);
    }

    /// <inheritdoc/>
    public string GetPublicUrl(string storageKey)
    {
        return _objectStorage.GetPublicUrl(StorageArea.ProductPhotos, storageKey);
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

    private static void MarkFailed(IPhotoRow photo, string errorMessage, DateTimeOffset now)
    {
        photo.Status = ProductPhotoStatus.Failed;
        photo.ErrorMessage = Truncate(errorMessage, 1000);
        photo.LastCheckedAt = now;
        photo.UpdatedAt = now;
    }

    // A failed download or upload must not degrade a photo that is already served: the CDN can be
    // unreachable while the stored bytes are still correct. The error is recorded for visibility only.
    private static void RecordCheckFailure(IPhotoRow photo, string errorMessage, DateTimeOffset now)
    {
        photo.ErrorMessage = Truncate(errorMessage, 1000);
        photo.LastCheckedAt = now;
        photo.UpdatedAt = now;
    }

    private async Task<ProductPhotoImportResult> ReconcileAsync(
        PhotoSet rowSet,
        Guid productId,
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
        // that every live photo of this set must be removed.
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
                    rowSet,
                    productId,
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
                    productId);
                result.Failed++;
            }
        }

        RemoveUndeclaredPhotos(rowSet, photos, productId, result);

        return result;
    }

    // Marks every live row of this set that the card no longer declares. The other set (canonical
    // versus another channel) is a different collection and is never touched from here.
    private void RemoveUndeclaredPhotos(
        PhotoSet rowSet,
        IReadOnlyList<MarketplacePhotoSource> photos,
        Guid productId,
        ProductPhotoImportResult result)
    {
        HashSet<string> declaredExternalIds = photos
            .Select(s => s.ExternalId)
            .ToHashSet(StringComparer.Ordinal);

        DateTimeOffset now = _timeProvider.GetUtcNow();

        List<IPhotoRow> undeclared = rowSet.GetRows()
            .Where(p => p.Status == ProductPhotoStatus.Uploaded
                        && !declaredExternalIds.Contains(p.ExternalId))
            .ToList();

        foreach (IPhotoRow photo in undeclared)
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

        bool anyPhotoLeft = rowSet.GetRows().Any(p => p.Status == ProductPhotoStatus.Uploaded);

        if (!anyPhotoLeft)
        {
            _logger.LogWarning(
                "Photo set of Product {ProductId} no longer declares any of the {Removed} photo(s) it held, so it now has no uploaded photos at all.",
                productId,
                undeclared.Count);
        }
    }

    // The live row of a slot in this set.
    private IPhotoRow? FindLivePhoto(PhotoSet rowSet, string externalId)
    {
        List<IPhotoRow> matches = rowSet.GetRows()
            .Where(p => p.Status != ProductPhotoStatus.Removed
                        && string.Equals(p.ExternalId, externalId, StringComparison.Ordinal))
            .OrderByDescending(p => p.CreatedAt)
            .ToList();

        if (matches.Count > 1)
        {
            _logger.LogWarning(
                "Photo set has {Count} live photo rows for ExternalId {ExternalId}; using the newest.",
                matches.Count,
                externalId);
        }

        return matches.Count == 0 ? null : matches[0];
    }

    // Reconciles one declared slot of one set. The bytes are always downloaded: the marketplace
    // keeps the URL when the image in a slot is replaced and exposes no digest, so the hash of the
    // payload is the only reliable change detector.
    private async Task<PhotoImportOutcome> ReconcilePhotoAsync(
        PhotoSet rowSet,
        Guid productId,
        MarketplacePhotoSource source,
        Guid tenantId,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        IPhotoRow? livePhoto = FindLivePhoto(rowSet, source.ExternalId);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        (PhotoPayload? download, string? failureReason) = await DownloadAsync(
            httpClient,
            source,
            productId,
            cancellationToken);

        if (download is null)
        {
            string reason = failureReason ?? "Photo download failed.";

            if (livePhoto is null)
            {
                IPhotoRow pendingPhoto = rowSet.AddRow(source);
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

        PhotoPayload payload = download.Value;
        string contentHash = Convert.ToHexString(SHA256.HashData(payload.Bytes)).ToLowerInvariant();

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
                livePhoto.LastCheckedAt = now;
                livePhoto.UpdatedAt = now;

                return PhotoImportOutcome.Skipped;
            }
        }

        string storageKey = ProductPhotoKeys.Build(tenantId, productId, contentHash);

        try
        {
            using MemoryStream content = new MemoryStream(payload.Bytes, writable: false);

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
                productId);

            string reason = "Upload to object storage failed: " + ex.Message;

            if (livePhoto is null)
            {
                IPhotoRow pendingPhoto = rowSet.AddRow(source);
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

        IPhotoRow uploaded = rowSet.AddRow(source);
        uploaded.ContentHash = contentHash;
        uploaded.StorageKey = storageKey;
        uploaded.SizeBytes = payload.Bytes.Length;
        uploaded.Width = payload.Width;
        uploaded.Height = payload.Height;
        uploaded.Status = ProductPhotoStatus.Uploaded;
        uploaded.LastCheckedAt = now;

        if (uploaded is ProductPhoto productPhoto)
        {
            productPhoto.OriginalFileName = $"{uploaded.Id:N}.{ProductPhotoKeys.Extension}";
        }

        if (livePhoto is null)
        {
            return PhotoImportOutcome.Imported;
        }

        // A superseded row that never held a blob is dropped instead of retained: the grace period
        // exists to keep a served object alive, and this row never had one.
        if (string.IsNullOrEmpty(livePhoto.StorageKey))
        {
            rowSet.RemoveRow(livePhoto);
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
    /// Downloads a photo, validates it and reads its rendered size. Returns a null download
    /// together with the reason when the bytes are unusable.
    /// </summary>
    private async Task<(PhotoPayload? Download, string? FailureReason)> DownloadAsync(
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

        // The header states the size of the bytes actually received, which is what the row must
        // record: a marketplace-declared size is a box, and the rendered image need not fill it.
        // The declared value stays as the fallback for a layout this parser does not understand.
        int? width = source.Width;
        int? height = source.Height;

        if (WebPDimensions.TryRead(payload, out int parsedWidth, out int parsedHeight))
        {
            width = parsedWidth;
            height = parsedHeight;
        }
        else
        {
            _logger.LogWarning(
                "Unreadable WebP chunk layout for Product {ProductId}, ExternalId {ExternalId}; keeping the declared size.",
                productId,
                source.ExternalId);
        }

        return (new PhotoPayload(payload, width, height), null);
    }

    // The downloaded bytes together with the size to persist: the rendered size read from the WebP
    // header, falling back to the marketplace-declared size when the header is not understood.
    private readonly record struct PhotoPayload(byte[] Bytes, int? Width, int? Height);

    /// <summary>
    /// A photo set being reconciled: how to read its current rows and how to add or drop one. The
    /// same pipeline then serves the canonical gallery and each channel's gallery.
    /// </summary>
    private sealed class PhotoSet
    {
        public required Func<IReadOnlyList<IPhotoRow>> GetRows { get; init; }

        public required Func<MarketplacePhotoSource, IPhotoRow> AddRow { get; init; }

        public required Action<IPhotoRow> RemoveRow { get; init; }
    }

    private enum PhotoImportOutcome
    {
        Imported,
        Replaced,
        Skipped,
        Failed
    }
}
