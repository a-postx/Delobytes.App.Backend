using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProductCostsBatch;

public class GetProductCostsBatchQueryHandler : IRequestHandler<GetProductCostsBatchQuery, GetProductCostsBatchResponse>
{
    private readonly ICostCalculator _costCalculator;

    public GetProductCostsBatchQueryHandler(ICostCalculator costCalculator)
    {
        _costCalculator = costCalculator;
    }

    public async Task<GetProductCostsBatchResponse> Handle(GetProductCostsBatchQuery request, CancellationToken cancellationToken)
    {
        DateOnly asOf = request.AsOf ?? DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);

        List<ProductCostSummaryDto> items = new List<ProductCostSummaryDto>();

        foreach (Guid productId in request.ProductIds)
        {
            CostBreakdown breakdown = await _costCalculator.CalculateAsync(productId, asOf, cancellationToken);

            items.Add(new ProductCostSummaryDto
            {
                ProductId = breakdown.ProductId,
                MaterialCost = breakdown.MaterialCost,
                LogisticsCost = breakdown.LogisticsCost,
                PackagingCost = breakdown.PackagingCost,
                LaborCost = breakdown.LaborCost,
                TotalCost = breakdown.TotalCost,
                IsComplete = breakdown.IsComplete,
            });
        }

        return new GetProductCostsBatchResponse
        {
            AsOfDate = asOf,
            Items = items,
        };
    }
}
