using MediatR;

namespace Delobytes.App.Backend.Integrations.Application.Commands.CancelSyncJob;

public class CancelSyncJobCommand : IRequest
{
    public Guid SyncJobId { get; set; }
}
