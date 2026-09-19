using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.ProductChannelCosts.UpdateProductChannelCost;

public class UpdateProductChannelCostCommand : IRequest<UpdateProductChannelCostResponse>, IRequireRole
{
    public Guid Id { get; set; }

    public decimal Amount { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
