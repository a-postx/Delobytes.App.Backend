namespace Delobytes.App.Backend.Options;

/// <summary>
/// Настройки доступа к S3-совместимому объектному хранилищу (Yandex Object Storage).
/// </summary>
public class ObjectStorageOptions
{
    /// <summary>
    /// Идентификатор ключа сервисного аккаунта. Заполняется из секретов, не из appsettings.json.
    /// </summary>
    public string AccessKeyId { get; set; } = default!;

    /// <summary>
    /// Секретный ключ сервисного аккаунта. Заполняется из секретов, не из appsettings.json.
    /// </summary>
    public string SecretAccessKey { get; set; } = default!;

    /// <summary>Key matches the StorageArea enum name, e.g. "ProductPhotos".</summary>
    public Dictionary<string, StorageAreaOptions> Areas { get; set; } = new();
}

/// <summary>
/// Настройки конкретной области хранения (бакета).
/// </summary>
public class StorageAreaOptions
{
    /// <summary>
    /// Имя бакета в объектном хранилище.
    /// </summary>
    public string Bucket { get; set; } = default!;

    /// <summary>
    /// Признак того, что область доступна по публичному URL.
    /// </summary>
    public bool IsPublic { get; set; }
}
