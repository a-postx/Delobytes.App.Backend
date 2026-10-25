using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Commands.ProductChannelCosts;

public class UpdateProductChannelCostCommand : IRequest<UpdateProductChannelCostResponse>, IRequireRole
{
    public Guid Id { get; set; }

    public decimal Amount { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}

public class UpdateProductChannelCostResponse
{
    public bool Found { get; set; }
}

public class UpdateProductChannelCostCommandHandler : IRequestHandler<UpdateProductChannelCostCommand, UpdateProductChannelCostResponse>
{
    private readonly IProductChannelCostRepository _repository;

    public UpdateProductChannelCostCommandHandler(IProductChannelCostRepository repository)
    {
        _repository = repository;
    }

    public async Task<UpdateProductChannelCostResponse> Handle(UpdateProductChannelCostCommand request, CancellationToken cancellationToken)
    {
        ProductChannelCost? cost = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (cost == null)
        {
            return new UpdateProductChannelCostResponse { Found = false };
        }

        cost.Amount = request.Amount;
        cost.UpdatedAt = DateTimeOffset.UtcNow;

        await _repository.SaveChangesAsync(cancellationToken);

        return new UpdateProductChannelCostResponse { Found = true };
    }
}
