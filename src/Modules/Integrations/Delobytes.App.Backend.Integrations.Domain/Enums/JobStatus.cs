namespace Delobytes.App.Backend.Integrations.Domain.Enums;

/// <summary>
/// Execution status of a <see cref="Entities.JobRun"/>.
/// </summary>
public enum JobStatus
{
    Pending = 0,
    Running = 1,
    Success = 2,
    Failed = 3,
    Cancelled = 4
}
