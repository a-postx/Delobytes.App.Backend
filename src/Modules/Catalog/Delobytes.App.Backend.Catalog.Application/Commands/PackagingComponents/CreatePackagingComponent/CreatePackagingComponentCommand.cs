using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Identity.Domain.Enums;
using Delobytes.App.Backend.Identity.Domain.Interfaces;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.PackagingComponents.CreatePackagingComponent;

public class CreatePackagingComponentCommand : IRequest<CreatePackagingComponentResponse>, IRequireRole
{
    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public Domain.Enums.Unit Unit { get; set; }

    public decimal PricePerUnit { get; set; }

    public string? Supplier { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
