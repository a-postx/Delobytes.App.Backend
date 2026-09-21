using Delobytes.App.Backend.Catalog.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProducts;

public class GetProductsQuery : IRequest<GetProductsResponse>
{
    /// <summary>Filter by status. Null returns only Active products.</summary>
    public ProductStatus? Status { get; set; }
}
