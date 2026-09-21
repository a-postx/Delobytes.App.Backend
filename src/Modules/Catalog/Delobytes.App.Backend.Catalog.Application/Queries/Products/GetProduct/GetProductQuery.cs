using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProduct;

public class GetProductQuery : IRequest<GetProductResponse>
{
    public Guid Id { get; set; }
}
