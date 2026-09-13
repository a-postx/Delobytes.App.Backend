using Delobytes.App.Backend.Identity.Domain.Enums;
using Delobytes.App.Backend.Identity.Domain.Interfaces;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.PackagingComponents.CreatePackagingComponentPrice;

/// <summary>
/// Adds a new price version for an existing packaging component. The previous active
/// version is deactivated; no existing price record is ever overwritten.
/// </summary>
public class CreatePackagingComponentPriceCommand : IRequest<CreatePackagingComponentPriceResponse>, IRequireRole
{
    public Guid PackagingComponentId { get; set; }

    public decimal PricePerUnit { get; set; }

    public Guid? SupplierId { get; set; }

    public DateOnly ValidFrom { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
