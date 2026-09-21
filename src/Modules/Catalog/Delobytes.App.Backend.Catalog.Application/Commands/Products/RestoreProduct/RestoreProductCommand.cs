using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Products.RestoreProduct;

public class RestoreProductCommand : IRequest<RestoreProductResponse>
{
    public Guid ProductId { get; set; }
}
