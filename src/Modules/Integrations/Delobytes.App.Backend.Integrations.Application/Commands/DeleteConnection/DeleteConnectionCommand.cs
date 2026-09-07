using MediatR;

namespace Delobytes.App.Backend.Integrations.Application.Commands.DeleteConnection;

public class DeleteConnectionCommand : IRequest
{
    public Guid Id { get; set; }
}
