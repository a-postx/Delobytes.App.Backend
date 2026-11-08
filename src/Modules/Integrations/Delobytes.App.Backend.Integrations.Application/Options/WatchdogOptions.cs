namespace Delobytes.App.Backend.Integrations.Application.Options;

/// <summary>
/// Operational configuration for WatchdogHostedService.
/// </summary>
public class WatchdogOptions
{
    /// <summary>
    /// How often WatchdogHostedService checks for Running jobs that exceeded their configured
    /// timeout, in minutes.
    /// </summary>
    public double IntervalMinutes { get; set; } = 5;
}
