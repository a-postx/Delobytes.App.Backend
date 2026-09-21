using Delobytes.App.Backend.Catalog.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProducts;

public class GetProductsQuery : IRequest<GetProductsResponse>
{
    /// <summary>
    /// Optional status filter. When omitted, products of all statuses are returned and the
    /// caller is responsible for filtering (the products page computes tab counters locally).
    /// </summary>
    public ProductStatus? Status { get; set; }
}
