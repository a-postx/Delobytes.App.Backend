using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.ProductChannelCosts.DeleteProductChannelCost;

public class DeleteProductChannelCostCommand : IRequest<DeleteProductChannelCostResponse>, IRequireRole
{
    public Guid Id { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
