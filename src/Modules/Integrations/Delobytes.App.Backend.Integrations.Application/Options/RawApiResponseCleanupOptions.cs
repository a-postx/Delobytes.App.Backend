namespace Delobytes.App.Backend.Integrations.Application.Options;

/// <summary>
/// Operational configuration for RawApiResponseCleanupHostedService (Stage 14). RawApiResponses
/// rows outlive nothing on their own — the application never deletes JobRuns, so the cascade
/// from JobRun never fires — which makes a time-based sweep the only bound on the table's growth.
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
