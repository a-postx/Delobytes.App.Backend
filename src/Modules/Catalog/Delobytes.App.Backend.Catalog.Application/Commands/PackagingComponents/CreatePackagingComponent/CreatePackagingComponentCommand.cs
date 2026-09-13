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

    /// <summary>Price of the first price version created together with the component.</summary>
    public decimal PricePerUnit { get; set; }

    public Guid? SupplierId { get; set; }

    /// <summary>Effective date of the first price version.</summary>
    public DateOnly ValidFrom { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
