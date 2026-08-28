namespace Delobytes.App.Backend.Options;

/// <summary>
/// Секреты приложения, получаемые из хранилища секретов (Yandex Cloud Lockbox).
/// </summary>
public class AppSecrets
{
    /// <summary>
    /// Строка подключения к базе данных (PostgreSQL).
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Строка подключения к CloudAMQP (RabbitMQ), формат: amqps://user:password@host/vhost
    /// </summary>
    public string? MessageBusConnectionString { get; set; }

    /// <summary>
    /// Адрес OpenSearch.
    /// </summary>
    public string? ElasticSearchUrl { get; set; }

    /// <summary>
    /// Пользователь OpenSearch.
    /// </summary>
    public string? ElasticSearchUser { get; set; }

    /// <summary>
    /// Пароль OpenSearch.
    /// </summary>
    public string? ElasticSearchPassword { get; set; }
}
