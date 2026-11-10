using Delobytes.App.Backend.Contracts.Interfaces;

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
    /// Gets or sets the job run identifier.
    /// </summary>
    public Guid JobRunId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the connection (marketplace cabinet) the call was made
    /// through. Null for rows captured before attribution was recorded.
    /// </summary>
    public Guid? ConnectionId { get; set; }

    /// <summary>
    /// Gets or sets the channel of <see cref="ConnectionId"/>. Denormalised on purpose: the
    /// connection may be reconfigured or removed, while the row must still say which channel it
    /// came from. Null for rows captured before attribution was recorded.
    /// </summary>
    public Guid? ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the channel's own request id (<c>x-request-id</c>), the value a marketplace
    /// support team asks for when a call has to be traced. Null when the channel did not send one,
    /// or when the call failed before a response existed.
    /// </summary>
    public string? ChannelRequestId { get; set; }

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
    /// Navigation property: the job run.
    /// </summary>
    public JobRun JobRun { get; set; } = default!;
}
