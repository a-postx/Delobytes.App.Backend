using Amazon.S3;
using Amazon.S3.Model;
using Delobytes.App.Backend.Contracts.Storage;
using Delobytes.App.Backend.Options;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Infrastructure.Storage;

/// <summary>
/// S3-совместимая реализация <see cref="IObjectStorage"/> для Yandex Object Storage.
/// </summary>
public class S3ObjectStorage : IObjectStorage
{
    /// <summary>
    /// Максимальное число объектов в одном запросе группового удаления S3.
    /// </summary>
    private const int MaxObjectsPerDeleteBatch = 1000;
    private readonly IAmazonS3 _client;
    private readonly ObjectStorageOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="S3ObjectStorage"/> class.
    /// </summary>
    /// <param name="client">Переиспользуемый (singleton) S3-клиент.</param>
    /// <param name="options">Настройки областей хранения.</param>
    public S3ObjectStorage(IAmazonS3 client, IOptions<ObjectStorageOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    /// <inheritdoc/>
    public async Task UploadAsync(
        StorageArea area,
        string key,
        Stream content,
        string contentType,
        string? cacheControl,
        CancellationToken cancellationToken)
    {
        StorageAreaOptions areaOptions = ResolveArea(area);

        PutObjectRequest request = new PutObjectRequest
        {
            BucketName = areaOptions.Bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType
        };

        if (!string.IsNullOrWhiteSpace(cacheControl))
        {
            request.Headers.CacheControl = cacheControl;
        }

        try
        {
            await _client.PutObjectAsync(request, cancellationToken);
        }
        catch (AmazonS3Exception ex)
        {
            throw new ObjectStorageException($"Failed to upload object '{key}' to area '{area}'.", ex);
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(StorageArea area, string key, CancellationToken cancellationToken)
    {
        StorageAreaOptions areaOptions = ResolveArea(area);

        try
        {
            await _client.DeleteObjectAsync(areaOptions.Bucket, key, cancellationToken);
        }
        catch (AmazonS3Exception ex)
        {
            throw new ObjectStorageException($"Failed to delete object '{key}' from area '{area}'.", ex);
        }
    }

    /// <inheritdoc/>
    public async Task DeleteManyAsync(StorageArea area, IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
        {
            return;
        }

        StorageAreaOptions areaOptions = ResolveArea(area);

        try
        {
            foreach (List<KeyVersion> batch in BuildDeleteBatches(keys))
            {
                DeleteObjectsRequest request = new DeleteObjectsRequest
                {
                    BucketName = areaOptions.Bucket,
                    Objects = batch
                };

                await _client.DeleteObjectsAsync(request, cancellationToken);
            }
        }
        catch (AmazonS3Exception ex)
        {
            throw new ObjectStorageException(
                $"Failed to batch-delete {keys.Count} object(s) from area '{area}'.", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(StorageArea area, string key, CancellationToken cancellationToken)
    {
        StorageAreaOptions areaOptions = ResolveArea(area);

        try
        {
            await _client.GetObjectMetadataAsync(areaOptions.Bucket, key, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (AmazonS3Exception ex)
        {
            throw new ObjectStorageException(
                $"Failed to check existence of object '{key}' in area '{area}'.", ex);
        }
    }

    /// <inheritdoc/>
    public string GetPublicUrl(StorageArea area, string key)
    {
        StorageAreaOptions areaOptions = ResolveArea(area);

        if (!areaOptions.IsPublic)
        {
            throw new InvalidOperationException($"Area '{area}' is not configured for public access.");
        }

        return $"https://{areaOptions.Bucket}.storage.yandexcloud.net/{key}";
    }

    private StorageAreaOptions ResolveArea(StorageArea area)
    {
        if (!_options.Areas.TryGetValue(area.ToString(), out StorageAreaOptions? areaOptions))
        {
            throw new InvalidOperationException($"Storage area '{area}' is not configured.");
        }

        return areaOptions;
    }

    private static IEnumerable<List<KeyVersion>> BuildDeleteBatches(IReadOnlyCollection<string> keys)
    {
        List<KeyVersion> batch = new List<KeyVersion>(MaxObjectsPerDeleteBatch);

        foreach (string key in keys)
        {
            batch.Add(new KeyVersion { Key = key });

            if (batch.Count == MaxObjectsPerDeleteBatch)
            {
                yield return batch;
                batch = new List<KeyVersion>(MaxObjectsPerDeleteBatch);
            }
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }
}
