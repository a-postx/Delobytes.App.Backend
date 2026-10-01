namespace Delobytes.App.Backend.Contracts.Accounting;

/// <summary>
/// Настройки учёта тенанта, не зависящие от версии налогового профиля.
/// </summary>
public sealed class TenantAccountingSettings
{
    /// <summary>
    /// Gets or sets код валюты учёта (ISO 4217).
    /// </summary>
    public string Currency { get; init; } = default!;

    /// <summary>
    /// Gets or sets идентификатор часового пояса тенанта (IANA).
    /// </summary>
    public string TimeZoneId { get; init; } = default!;
}
