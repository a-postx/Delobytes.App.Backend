using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Application.DTOs.OrdersImports;

/// <summary>
/// API representation of an OrdersImport job run, mapped from JobRun + OrdersImportJob.
/// Mirrors <see cref="Delobytes.App.Backend.Integrations.Application.DTOs.ProductsImports.ProductsImportDto"/>:
/// OrdersImportJob currently carries no type-specific fields of its own (see its XML docs).
/// </summary>
public class OrdersImportDto
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
