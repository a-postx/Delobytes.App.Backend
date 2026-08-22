using System.ComponentModel.DataAnnotations;

namespace Delobytes.App.Backend.Options;

/// <summary>
/// Конфигурационные параметры Auth0 (identity-провайдер).
/// Значения берутся из appsettings.json, раздел "Auth0".
/// </summary>
public class Auth0Options
{
    /// <summary>
    /// Домен Auth0-приложения (tenant).
    /// Пример: "your-tenant.auth0.com"
    /// </summary>
    [Required]
    public string Domain { get; set; } = string.Empty;

    /// <summary>
    /// Audience JWT-токена (API Identifier в Auth0).
    /// Пример: "https://api.delobytes.io"
    /// </summary>
    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Полный URL эмитента (issuer), формируется как "https://{Domain}/".
    /// </summary>
    public string Authority => $"https://{Domain}/";
}
