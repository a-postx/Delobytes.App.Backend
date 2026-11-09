using Delobytes.App.Backend.Catalog.Application.Services;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Products;

public class RequestProductDeletionCommand : IRequest<RequestProductDeletionResponse>
{
    public Guid ProductId { get; set; }
}

public class RequestProductDeletionResponse
{
    public bool Found { get; set; }

    /// <summary>False when the product state prevents initiating deletion.</summary>
    public bool Accepted { get; set; }

    public string? ErrorMessage { get; set; }

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
