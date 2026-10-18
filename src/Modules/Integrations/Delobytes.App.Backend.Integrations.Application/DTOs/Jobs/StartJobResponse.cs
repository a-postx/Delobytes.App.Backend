namespace Delobytes.App.Backend.Integrations.Application.DTOs.Jobs;

/// <summary>
/// Response of POST /jobs/{type}.
/// </summary>
public class StartJobResponse
{
    public Guid JobRunId { get; set; }

    /// <summary>
    /// Gets or sets the API type token of the started job.
    /// </summary>
    public string Type { get; set; } = string.Empty;
}
