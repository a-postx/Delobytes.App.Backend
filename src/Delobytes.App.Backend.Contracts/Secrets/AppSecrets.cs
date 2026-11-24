namespace Delobytes.App.Backend.Contracts.Secrets;

/// <summary>
/// Application secrets loaded from the external secret store (Yandex Cloud Lockbox).
/// </summary>
public class AppSecrets
{
    /// <summary>
    /// PostgreSQL connection string.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// CloudAMQP (RabbitMQ) connection string, format: amqps://user:password@host/vhost.
    /// </summary>
    public string? MessageBusConnectionString { get; set; }

    /// <summary>
    /// OpenSearch endpoint.
    /// </summary>
    public string? ElasticSearchUrl { get; set; }

    /// <summary>
    /// OpenSearch user.
    /// </summary>
    public string? ElasticSearchUser { get; set; }

    /// <summary>
    /// OpenSearch password.
    /// </summary>
    public string? ElasticSearchPassword { get; set; }

    /// <summary>
    /// JWT signing key.
    /// </summary>
    public string? JwtSecretKey { get; set; }

    /// <summary>
    /// Yandex OAuth client id.
    /// </summary>
    public string? YandexClientId { get; set; }

    /// <summary>
    /// Yandex OAuth client secret.
    /// </summary>
    public string? YandexClientSecret { get; set; }

    /// <summary>
    /// Google OAuth client id.
    /// </summary>
    public string? GoogleClientId { get; set; }

    /// <summary>
    /// Google OAuth client secret.
    /// </summary>
    public string? GoogleClientSecret { get; set; }

    /// <summary>Static access key of the service account used for Object Storage.</summary>
    public string? ObjectStorageAccessKeyId { get; set; }

    /// <summary>Secret key of the service account used for Object Storage.</summary>
    public string? ObjectStorageSecretAccessKey { get; set; }
}
