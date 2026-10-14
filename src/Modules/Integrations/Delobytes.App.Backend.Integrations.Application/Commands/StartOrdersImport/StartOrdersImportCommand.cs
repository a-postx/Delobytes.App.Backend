using MediatR;

namespace Delobytes.App.Backend.Integrations.Application.Commands.StartOrdersImport;

public class StartOrdersImportCommand : IRequest<StartOrdersImportResponse>
{
    public Guid ConnectionId { get; set; }
}
