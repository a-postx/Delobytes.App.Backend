using MediatR;

namespace Delobytes.App.Backend.Integrations.Application.Commands;

public class StartProductsImportCommand : IRequest<StartProductsImportResponse>
{
    public Guid ConnectionId { get; set; }
}
