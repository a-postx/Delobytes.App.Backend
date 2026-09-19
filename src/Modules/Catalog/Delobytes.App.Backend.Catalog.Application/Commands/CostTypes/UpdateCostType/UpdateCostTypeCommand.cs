using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.CostTypes.UpdateCostType;

public class UpdateCostTypeCommand : IRequest<UpdateCostTypeResponse>, IRequireRole
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
