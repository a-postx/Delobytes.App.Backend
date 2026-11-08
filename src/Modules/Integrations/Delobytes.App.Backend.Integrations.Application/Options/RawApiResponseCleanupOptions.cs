namespace Delobytes.App.Backend.Integrations.Application.Options;

/// <summary>
/// Operational configuration for RawApiResponseCleanupHostedService.
/// </summary>
public class RawApiResponseCleanupOptions
{
    /// <summary>
    /// Age in days past which a raw API response is eligible for deletion, measured from
    /// <see cref="Delobytes.App.Backend.Integrations.Domain.Entities.RawApiResponse.ReceivedAt"/>.
    /// </summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>
    /// How often the cleanup sweep runs, in hours.
    /// </summary>
    public int IntervalHours { get; set; } = 24;
}
