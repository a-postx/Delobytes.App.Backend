namespace Delobytes.App.Backend.Integrations.Application.DTOs.Jobs;

/// <summary>
/// Paginated list of job runs for the current tenant.
/// </summary>
public class GetJobsResponse
{
    public List<JobDto> Jobs { get; set; } = new List<JobDto>();

    /// <summary>
    /// Gets or sets the total number of job runs matching the applied filters, ignoring paging.
    /// </summary>
    public int TotalCount { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }
}
