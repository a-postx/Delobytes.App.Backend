using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Delobytes.App.Backend.Options;

/// <summary>
/// Настройки приложения.
/// </summary>
public class AppSettings
{
    public AppSettings()
    {

    }

    [Required]
    public KestrelServerOptions Kestrel { get; set; } = default!;
}
