using Delobytes.App.Backend.Catalog.Application.Services;
using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Products;

public class RequestProductDeletionCommand : IRequest<RequestProductDeletionResponse>, IRequireRole
{
    /// <inheritdoc/>
    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };

    public Guid ProductId { get; set; }
}

public class RequestProductDeletionResponse
{
    public Guid ProductId { get; set; }
}

public class RequestProductDeletionCommandHandler : IRequestHandler<RequestProductDeletionCommand, RequestProductDeletionResponse>
{
    private readonly ProductDeletionService _deletionService;

    public RequestProductDeletionCommandHandler(ProductDeletionService deletionService)
    {
        _deletionService = deletionService;
    }

    public Task<RequestProductDeletionResponse> Handle(
        RequestProductDeletionCommand request,
        CancellationToken cancellationToken)
    {
        return _deletionService.RequestDeletionAsync(request.ProductId, cancellationToken);
    }
}
