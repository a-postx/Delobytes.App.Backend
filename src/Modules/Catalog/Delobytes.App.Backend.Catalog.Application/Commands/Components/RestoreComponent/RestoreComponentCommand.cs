using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Components.RestoreComponent;

public class RestoreComponentCommand : IRequest<RestoreComponentResponse>, IRequireRole
{
    public Guid Id { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
