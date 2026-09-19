using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.ProductChannelCosts.CreateProductChannelCost;

public class CreateProductChannelCostCommand : IRequest<CreateProductChannelCostResponse>, IRequireRole
{
    public Guid ProductId { get; set; }

    public Guid ChannelId { get; set; }

    public Guid CostTypeId { get; set; }

    public decimal Amount { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
