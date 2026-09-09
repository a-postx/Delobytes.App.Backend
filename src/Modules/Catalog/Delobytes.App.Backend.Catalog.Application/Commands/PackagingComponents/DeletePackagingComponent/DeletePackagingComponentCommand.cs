using Delobytes.App.Backend.Identity.Domain.Enums;
using Delobytes.App.Backend.Identity.Domain.Interfaces;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.PackagingComponents.DeletePackagingComponent;

public class DeletePackagingComponentCommand : IRequest<DeletePackagingComponentResponse>, IRequireRole
{
    public Guid Id { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
