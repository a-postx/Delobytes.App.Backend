using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Application.DTOs.ProductsImports;

/// <summary>
/// API representation of a ProductsImport job run, mapped from JobRun + ProductsImportJob.
/// </summary>
public class ProductsImportDto
{
    public Guid Id { get; set; }
    public Guid ConnectionId { get; set; }
    public JobStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Error { get; set; }
    public int RecordsCreated { get; set; }
    public int RecordsUpdated { get; set; }
    public int RecordsSkipped { get; set; }
    public int RecordsFailed { get; set; }
    public Guid? RequestedByUserId { get; set; }
}
