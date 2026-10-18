using Delobytes.App.Backend.Integrations.Application.DTOs.Jobs;
using MediatR;

namespace Delobytes.App.Backend.Integrations.Application.Queries.GetJob;

/// <summary>
/// Returns a single job run of any type by its identifier.
/// </summary>
public class GetJobQuery : IRequest<JobDto>
{
    public Guid JobRunId { get; set; }
}
