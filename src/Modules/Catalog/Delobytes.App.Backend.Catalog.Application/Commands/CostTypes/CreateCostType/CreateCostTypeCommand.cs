using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.CostTypes.CreateCostType;

public class CreateCostTypeCommand : IRequest<CreateCostTypeResponse>, IRequireRole
{
    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
