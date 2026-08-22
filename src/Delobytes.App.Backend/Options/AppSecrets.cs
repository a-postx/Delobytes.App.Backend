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
    /// URL Grafana Cloud Loki endpoint.
    /// Пример: https://logs-prod-{region}.grafana.net
    /// </summary>
    public string? LokiUrl { get; set; }

    /// <summary>
    /// Логин (Grafana Cloud Org ID) для аутентификации в Loki.
    /// </summary>
    public string? LokiUser { get; set; }

    /// <summary>
    /// Пароль (API-токен) для аутентификации в Loki.
    /// </summary>
    public string? LokiPassword { get; set; }
}
