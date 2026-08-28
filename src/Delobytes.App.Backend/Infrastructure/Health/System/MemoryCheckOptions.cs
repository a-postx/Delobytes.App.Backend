namespace Delobytes.App.Backend.Infrastructure.Health.System;

/// <summary>
/// Настройки памяти для проверки здоровья.
/// </summary>
public class MemoryCheckOptions
{
    public int ProcessMaxMemoryThreshold { get; set; } = 2048;
}
