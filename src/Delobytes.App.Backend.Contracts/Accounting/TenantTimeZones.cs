namespace Delobytes.App.Backend.Contracts.Accounting;

/// <summary>
/// Преобразования между UTC и календарным временем тенанта.
/// </summary>
public static class TenantTimeZones
{
    /// <summary>
    /// Часовой пояс по умолчанию для новых тенантов.
    /// </summary>
    public const string DefaultTimeZoneId = "Europe/Moscow";

    /// <summary>
    /// Разбирает идентификатор часового пояса.
    /// </summary>
    /// <param name="timeZoneId">Идентификатор пояса (IANA, например <c>Europe/Moscow</c>).</param>
    /// <returns>Часовой пояс.</returns>
    /// <exception cref="InvalidOperationException">
    /// Пояс не задан, не найден в системе или содержит некорректные данные. Ошибка не глушится:
    /// молчаливый переход на UTC сдвинул бы календарные даты учёта, а это хуже явного сбоя.
    /// </exception>
    public static TimeZoneInfo Resolve(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            throw new InvalidOperationException("Часовой пояс тенанта не задан.");
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException ex)
        {
            throw new InvalidOperationException($"Часовой пояс '{timeZoneId}' не найден в системе.", ex);
        }
        catch (InvalidTimeZoneException ex)
        {
            throw new InvalidOperationException($"Часовой пояс '{timeZoneId}' содержит некорректные данные.", ex);
        }
    }

    /// <summary>
    /// Проверяет, распознаёт ли система указанный идентификатор пояса.
    /// </summary>
    /// <param name="timeZoneId">Идентификатор пояса.</param>
    /// <returns><c>true</c>, если пояс можно использовать.</returns>
    public static bool IsValid(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return false;
        }

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    /// <summary>
    /// Разбирает идентификатор пояса и откатывается на UTC, если он не распознан.
    /// Применяется только там, где UTC-фолбэк уже является согласованным поведением
    /// (например, планировщик), и никогда — при расчёте календарных дат учёта.
    /// </summary>
    /// <param name="timeZoneId">Идентификатор пояса.</param>
    /// <returns>Часовой пояс или UTC.</returns>
    public static TimeZoneInfo ResolveOrUtc(string? timeZoneId)
    {
        return IsValid(timeZoneId) ? Resolve(timeZoneId) : TimeZoneInfo.Utc;
    }

    /// <summary>
    /// Возвращает календарную дату тенанта для указанного момента времени.
    /// </summary>
    /// <param name="moment">Момент времени.</param>
    /// <param name="timeZone">Часовой пояс тенанта.</param>
    /// <returns>Дата в календаре тенанта.</returns>
    public static DateOnly DateAt(DateTimeOffset moment, TimeZoneInfo timeZone)
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, timeZone).DateTime);
    }

    /// <summary>
    /// Возвращает момент начала календарного дня тенанта в UTC.
    /// </summary>
    /// <param name="localDate">Календарная дата тенанта.</param>
    /// <param name="timeZone">Часовой пояс тенанта.</param>
    /// <returns>Начало дня, выраженное в UTC.</returns>
    public static DateTimeOffset StartOfDayUtc(DateOnly localDate, TimeZoneInfo timeZone)
    {
        DateTime localMidnight = DateTime.SpecifyKind(
            localDate.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified);

        return TimeZoneInfo.ConvertTimeToUtc(localMidnight, timeZone);
    }
}
