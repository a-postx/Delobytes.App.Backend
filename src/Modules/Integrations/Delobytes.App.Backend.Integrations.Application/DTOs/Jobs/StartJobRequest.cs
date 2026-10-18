namespace Delobytes.App.Backend.Integrations.Application.DTOs.Jobs;

/// <summary>
/// Body of POST /jobs/{type}.
/// </summary>
public class StartJobRequest
{
    public Guid ConnectionId { get; set; }
}
