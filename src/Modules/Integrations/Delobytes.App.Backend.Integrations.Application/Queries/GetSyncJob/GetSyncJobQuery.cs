using Delobytes.App.Backend.Integrations.Application.DTOs.SyncJobs;
using MediatR;

namespace Delobytes.App.Backend.Integrations.Application.Queries.GetSyncJob;

public class GetSyncJobQuery : IRequest<GetSyncJobResponse>
{
    public Guid SyncJobId { get; set; }
}
