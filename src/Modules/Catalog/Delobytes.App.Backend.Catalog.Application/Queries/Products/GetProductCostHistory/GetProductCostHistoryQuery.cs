using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProductCostHistory;

public class GetProductCostHistoryQuery : IRequest<GetProductCostHistoryResponse>
{
    public Guid ProductId { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; } = 50;
}
