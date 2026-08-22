namespace Delobytes.App.Backend.Messaging.Events;

/// <summary>
/// Тестовое событие, публикуемое при успешном запуске приложения.
/// </summary>
public record AppStartedEvent
{
    /// <summary>
    /// Уникальный идентификатор экземпляра события.
    /// </summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Момент времени, когда приложение завершило инициализацию.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Версия приложения.
    /// </summary>
    public string ApplicationVersion { get; init; } = string.Empty;
}
