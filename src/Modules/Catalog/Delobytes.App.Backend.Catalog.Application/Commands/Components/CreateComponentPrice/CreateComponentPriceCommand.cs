using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Components.CreateComponentPrice;

/// <summary>
/// Adds a new price version for an existing packaging component. The previous active
/// version is deactivated; no existing price record is ever overwritten.
/// </summary>
public class CreateComponentPriceCommand : IRequest<CreateComponentPriceResponse>, IRequireRole
{
    public Guid ComponentId { get; set; }

    public decimal PricePerUnit { get; set; }

    public Guid? SupplierId { get; set; }

    public DateOnly ValidFrom { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
