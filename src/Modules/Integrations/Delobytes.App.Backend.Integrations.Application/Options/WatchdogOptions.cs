namespace Delobytes.App.Backend.Integrations.Application.Options;

/// <summary>
/// Operational configuration for WatchdogHostedService (Stage 12). Per-type timeout values
/// live in the database instead (JobTypeConfiguration.TimeoutMinutes) since they differ per
/// JobType and are looked up by other components too.
/// </summary>
public class WatchdogOptions
{
    /// <summary>
    /// How often WatchdogHostedService checks for Running jobs that exceeded their configured
    /// timeout, in minutes.
    /// </summary>
    public double IntervalMinutes { get; set; } = 5;
}
