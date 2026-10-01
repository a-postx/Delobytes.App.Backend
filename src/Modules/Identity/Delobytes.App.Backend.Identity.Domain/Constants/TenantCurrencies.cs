namespace Delobytes.App.Backend.Identity.Domain.Constants;

/// <summary>
/// Допустимые валюты учёта тенанта. В версии 1 поддерживается только рубль.
/// </summary>
public static class TenantCurrencies
{
    /// <summary>Российский рубль.</summary>
    public const string Rub = "RUB";

    /// <summary>Список поддерживаемых кодов валют.</summary>
    public static readonly IReadOnlyList<string> Supported = new[] { Rub };
}
