using Delobytes.App.Backend.Identity.Domain.Interfaces;

namespace Delobytes.App.Backend.Integrations.Domain.Entities;

/// <summary>
/// Represents a tenant-scoped raw API response.
/// </summary>
public class RawApiResponse : ITenantScoped
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the sync job identifier.
    /// </summary>
    public Guid SyncJobId { get; set; }

    /// <summary>
    /// Gets or sets the API endpoint.
    /// </summary>
    public string Endpoint { get; set; } = default!;

    /// <summary>
    /// Gets or sets the request payload (JSON).
    /// </summary>
    public string RequestPayload { get; set; } = default!;

    /// <summary>
    /// Gets or sets the response payload (JSON).
    /// </summary>
    public string ResponsePayload { get; set; } = default!;

    /// <summary>
    /// Gets or sets the HTTP status code.
    /// </summary>
    public int HttpStatusCode { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the response was received.
    /// </summary>
    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the response was processed.
    /// </summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>
    /// Navigation property: the sync job.
    /// </summary>
    public SyncJob SyncJob { get; set; } = default!;
}
