namespace Delobytes.App.Backend.Infrastructure.Health.System;

/// <summary>
/// Настройки проверки доступности сети.
/// </summary>
public class NetworkCheckOptions
{
    public int MaxLatencyThreshold { get; set; } = 500;

    public string InternetHost { get; } = "77.88.8.8";
}
