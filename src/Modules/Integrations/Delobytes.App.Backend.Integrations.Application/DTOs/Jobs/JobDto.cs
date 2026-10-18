using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Application.DTOs.Jobs;

/// <summary>
/// API representation of a single job run regardless of its type, mapped from JobRun.
/// Type is serialised as a camelCase token ("products-import", "orders-import") so that the
/// client never depends on enum member names.
/// </summary>
public class JobDto
{
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the API type token: "products-import", "orders-import" or "orders-sync".
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the connection the job run belongs to. Returned even though the spec's
    /// minimal field set omits it: a client paginating a tenant-wide list cannot otherwise tell
    /// which connection a job belongs to.
    /// </summary>
    public Guid ConnectionId { get; set; }

    public JobStatus Status { get; set; }

    public int RecordsCreated { get; set; }

    public int RecordsUpdated { get; set; }

    public int RecordsSkipped { get; set; }

    public int RecordsFailed { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? Error { get; set; }
}
