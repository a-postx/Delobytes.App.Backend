using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProductCost;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Errors;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Products.PreviewProductBomCost;

public class PreviewProductBomCostQueryHandler : IRequestHandler<PreviewProductBomCostQuery, PreviewProductBomCostResponse>
{
    private readonly IProductRepository _productRepository;
    private readonly IComponentRepository _componentRepository;
    private readonly ICostCalculator _costCalculator;

    public PreviewProductBomCostQueryHandler(
        IProductRepository productRepository,
        IComponentRepository componentRepository,
        ICostCalculator costCalculator)
    {
        _productRepository = productRepository;
        _componentRepository = componentRepository;
        _costCalculator = costCalculator;
    }

    public async Task<PreviewProductBomCostResponse> Handle(
        PreviewProductBomCostQuery request,
        CancellationToken cancellationToken)
    {
        Product? product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);

        if (product == null)
        {
            return new PreviewProductBomCostResponse { Found = false };
        }

        // The draft is rejected exactly like a save would be, so the preview cannot show a figure
        // the save would later refuse to produce.
        if (request.Lines.Any(line => line.Quantity <= 0m))
        {
            throw new AppException(ErrorCodes.Catalog.BomLineInvalidQuantity);
        }

        if (request.Lines.Select(line => line.ComponentId).Distinct().Count() != request.Lines.Count)
        {
            throw new AppException(ErrorCodes.Common.ValidationFailed);
        }

        List<CostCalculationLine> lines = new List<CostCalculationLine>(request.Lines.Count);

        foreach (PreviewProductBomCostLine line in request.Lines)
        {
            Component? component = await _componentRepository.GetByIdAsync(line.ComponentId, cancellationToken);

            if (component == null)
            {
                throw new AppException(ErrorCodes.Catalog.BomComponentNotFound);
            }

            lines.Add(new CostCalculationLine(line.ComponentId, component, line.Quantity));
        }

        DateOnly asOf = request.AsOf ?? DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);

        CostBreakdown breakdown = await _costCalculator.CalculateForLinesAsync(
            request.ProductId,
            asOf,
            lines,
            cancellationToken);

        return new PreviewProductBomCostResponse
        {
            Found = true,
            Preview = ProductCostResponseMapper.Map(breakdown),
        };
    }
}
