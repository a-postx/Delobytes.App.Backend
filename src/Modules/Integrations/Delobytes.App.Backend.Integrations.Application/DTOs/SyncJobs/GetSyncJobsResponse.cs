namespace Delobytes.App.Backend.Integrations.Application.DTOs.SyncJobs;

public class GetSyncJobsResponse
{
    public List<SyncJobDto> Items { get; set; } = new List<SyncJobDto>();
}
