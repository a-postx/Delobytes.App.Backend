using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.Products;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProductCost;

public class GetProductCostQueryHandler : IRequestHandler<GetProductCostQuery, GetProductCostResponse>
{
    private readonly IProductRepository _productRepository;
    private readonly ICostCalculator _costCalculator;

    public GetProductCostQueryHandler(IProductRepository productRepository, ICostCalculator costCalculator)
    {
        _productRepository = productRepository;
        _costCalculator = costCalculator;
    }

    public async Task<GetProductCostResponse> Handle(GetProductCostQuery request, CancellationToken cancellationToken)
    {
        Product? product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);

        if (product == null)
        {
            return new GetProductCostResponse { Found = false };
        }

        // The calculator itself never reads the clock, so "today" is settled here and passed down
        // as an explicit date.
        DateOnly asOf = request.AsOf ?? DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);

        CostBreakdown breakdown = await _costCalculator.CalculateAsync(request.ProductId, asOf, cancellationToken);

        return ProductCostResponseMapper.Map(breakdown);
    }
}
