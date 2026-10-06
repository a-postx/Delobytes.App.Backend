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

        List<CostCalculationLine> lines = await ResolveDraftLinesAsync(request.Lines, cancellationToken);

        DateOnly asOf = request.AsOf ?? DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);

        CostBreakdown previewBreakdown = await _costCalculator.CalculateForLinesAsync(
            request.ProductId,
            asOf,
            lines,
            cancellationToken);

        // The baseline is priced in the same request, against the same date, so "было" and "стало"
        // always describe one consistent picture even if someone else saves the composition meanwhile.
        CostBreakdown baselineBreakdown = await _costCalculator.CalculateAsync(
            request.ProductId,
            asOf,
            cancellationToken);

        return new PreviewProductBomCostResponse
        {
            Found = true,
            Preview = ProductCostResponseMapper.Map(previewBreakdown),
            Baseline = MapBaseline(baselineBreakdown),
            Delta = MapDelta(previewBreakdown, baselineBreakdown),
        };
    }

    /// <summary>
    /// Resolves the whole draft with one component lookup and restores the order the client sent,
    /// because the batch query is free to return rows in any order and the preview highlights rows by position.
    /// </summary>
    private async Task<List<CostCalculationLine>> ResolveDraftLinesAsync(
        List<PreviewProductBomCostLine> draftLines,
        CancellationToken cancellationToken)
    {
        List<Guid> componentIds = draftLines.Select(line => line.ComponentId).ToList();

        IReadOnlyList<Component> components = await _componentRepository.GetByIdsAsync(componentIds, cancellationToken);

        Dictionary<Guid, Component> componentsById = components.ToDictionary(component => component.Id);

        List<CostCalculationLine> lines = new List<CostCalculationLine>(draftLines.Count);

        foreach (PreviewProductBomCostLine line in draftLines)
        {
            if (!componentsById.TryGetValue(line.ComponentId, out Component? component))
            {
                throw new AppException(ErrorCodes.Catalog.BomComponentNotFound);
            }

            lines.Add(new CostCalculationLine(line.ComponentId, component, line.Quantity));
        }

        return lines;
    }

    private static PreviewProductBomCostBaseline MapBaseline(CostBreakdown breakdown)
    {
        return new PreviewProductBomCostBaseline
        {
            MaterialCost = breakdown.MaterialCost,
            LogisticsCost = breakdown.LogisticsCost,
            PackagingCost = breakdown.PackagingCost,
            LaborCost = breakdown.LaborCost,
            TotalCost = breakdown.TotalCost,
            IsComplete = breakdown.IsComplete,
        };
    }

    private static PreviewProductBomCostDelta MapDelta(CostBreakdown preview, CostBreakdown baseline)
    {
        return new PreviewProductBomCostDelta
        {
            MaterialDelta = preview.MaterialCost - baseline.MaterialCost,
            LogisticsDelta = preview.LogisticsCost - baseline.LogisticsCost,
            PackagingDelta = preview.PackagingCost - baseline.PackagingCost,
            LaborDelta = preview.LaborCost - baseline.LaborCost,
            TotalDelta = preview.TotalCost - baseline.TotalCost,
        };
    }
}
