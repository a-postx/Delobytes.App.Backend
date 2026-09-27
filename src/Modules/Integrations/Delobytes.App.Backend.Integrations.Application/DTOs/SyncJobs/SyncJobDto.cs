namespace Delobytes.App.Backend.Integrations.Application.DTOs.SyncJobs;

public class SyncJobDto
{
    public Guid Id { get; set; }
    public Guid ConnectionId { get; set; }
    public string JobType { get; set; } = default!;
    public string Status { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public int RecordsProcessed { get; set; }
    public int RecordsImported { get; set; }
    public int RecordsCreated { get; set; }
    public int RecordsUpdated { get; set; }
    public int RecordsSkipped { get; set; }
    public int RecordsFailed { get; set; }
    public Guid? RequestedByUserId { get; set; }
}
