using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Commands.ProductChannelCosts;

public class DeleteProductChannelCostCommand : IRequest<DeleteProductChannelCostResponse>, IRequireRole
{
    public Guid Id { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}

public class DeleteProductChannelCostResponse
{
    public bool Found { get; set; }
}

public class DeleteProductChannelCostCommandHandler
    : IRequestHandler<DeleteProductChannelCostCommand, DeleteProductChannelCostResponse>
{
    private readonly IProductChannelCostRepository _repository;

    public DeleteProductChannelCostCommandHandler(IProductChannelCostRepository repository)
    {
        _repository = repository;
    }

    public async Task<DeleteProductChannelCostResponse> Handle(
        DeleteProductChannelCostCommand request,
        CancellationToken cancellationToken)
    {
        ProductChannelCost? cost = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (cost == null)
        {
            return new DeleteProductChannelCostResponse { Found = false };
        }

        _repository.Remove(cost);
        await _repository.SaveChangesAsync(cancellationToken);

        return new DeleteProductChannelCostResponse { Found = true };
    }
}
