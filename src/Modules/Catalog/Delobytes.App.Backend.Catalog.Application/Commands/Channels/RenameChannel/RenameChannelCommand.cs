using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Channels.RenameChannel;

public class RenameChannelCommand : IRequest<RenameChannelResponse>, IRequireRole
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
