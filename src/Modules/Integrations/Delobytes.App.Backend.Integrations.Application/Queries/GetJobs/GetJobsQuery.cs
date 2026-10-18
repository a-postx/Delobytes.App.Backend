using Delobytes.App.Backend.Integrations.Application.DTOs.Jobs;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Integrations.Application.Queries.GetJobs;

/// <summary>
/// Tenant-scoped listing of job runs of any type, with optional type/connection/status filters
/// and optional paging.
/// </summary>
public class GetJobsQuery : IRequest<GetJobsResponse>
{
    /// <summary>
    /// Gets or sets the API type token filter ("products-import", "orders-import"). Null means
    /// "all types"; an unrecognised token is rejected instead of silently returning everything.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// Gets or sets the connection filter. Null means "all connections of the tenant".
    /// </summary>
    public Guid? ConnectionId { get; set; }

    /// <summary>
    /// Gets or sets the status filter. Null or empty means "all statuses".
    /// </summary>
    public JobStatus[]? Status { get; set; }

    /// <summary>
    /// Gets or sets the 1-based page number. Null means "no paging": every matching job run is
    /// returned, which is the contract the previous product-imports listing had.
    /// </summary>
    public int? Page { get; set; }

    /// <summary>
    /// Gets or sets the page size, clamped to 1..200. Defaults to 50 and is only applied when
    /// <see cref="Page"/> is set.
    /// </summary>
    public int? PageSize { get; set; }
}
