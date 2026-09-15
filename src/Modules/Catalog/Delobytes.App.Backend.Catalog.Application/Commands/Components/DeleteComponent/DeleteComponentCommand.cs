using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Components.DeleteComponent;

public class DeleteComponentCommand : IRequest<DeleteComponentResponse>, IRequireRole
{
    public Guid Id { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
