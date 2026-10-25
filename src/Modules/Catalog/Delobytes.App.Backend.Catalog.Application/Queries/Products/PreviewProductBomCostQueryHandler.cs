using MediatR;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Errors;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Products;

/// <summary>
/// Requests the cost breakdown of a product composition that has not been saved yet.
/// Exists so the editor can show the effect of an edit before committing it: the draft is priced
/// through the same calculator as the persisted composition, and nothing is written.
/// </summary>
public class PreviewProductBomCostQuery : IRequest<PreviewProductBomCostResponse>
{
    /// <summary>Gets or sets the product the draft composition belongs to.</summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// Gets or sets the draft composition. An empty list is a valid draft that means "no composition",
    /// which prices as a missing BOM and still reports labour.
    /// </summary>
    public List<PreviewProductBomCostLine> Lines { get; set; } = new List<PreviewProductBomCostLine>();

    /// <summary>
    /// Gets or sets the date the input versions are resolved against.
    /// Null means today, which is the only case where the current clock is consulted.
    /// </summary>
    public DateOnly? AsOf { get; set; }
}

/// <summary>
/// One draft composition position supplied by the client.
/// </summary>
public class PreviewProductBomCostLine
{
    /// <summary>Gets or sets the component identifier.</summary>
    public Guid ComponentId { get; set; }

    /// <summary>Gets or sets the component quantity per one unit of the product.</summary>
    public decimal Quantity { get; set; }
}

/// <summary>
/// Cost breakdown of an unsaved composition, together with the persisted composition it was compared against.
/// Both sides are produced from a single read of the database, so they cannot drift apart the way two
/// independent requests would when the composition is edited in parallel.
/// </summary>
public class PreviewProductBomCostResponse
{
    /// <summary>Gets or sets a value indicating whether the product exists.</summary>
    public bool Found { get; set; }

    /// <summary>Gets or sets the cost breakdown of the draft.</summary>
    public GetProductCostResponse? Preview { get; set; }

    /// <summary>Gets or sets the summary of the persisted composition on the same date.</summary>
    public PreviewProductBomCostBaseline? Baseline { get; set; }

    /// <summary>Gets or sets the difference between the draft and the persisted composition.</summary>
    public PreviewProductBomCostDelta? Delta { get; set; }
}

/// <summary>
/// Summary of the persisted composition, reduced to the buckets the editor shows.
/// Deliberately not <see cref="GetProductCostResponse"/>: the baseline is only ever compared against,
/// never displayed line by line, so shipping its BOM lines would duplicate the whole composition on
/// every debounced keystroke for nothing.
/// </summary>
public class PreviewProductBomCostBaseline
{
    /// <summary>Gets or sets the cost of material components.</summary>
    public decimal MaterialCost { get; set; }

    /// <summary>Gets or sets the cost of logistics components.</summary>
    public decimal LogisticsCost { get; set; }

    /// <summary>Gets or sets the cost of packaging components.</summary>
    public decimal PackagingCost { get; set; }

    /// <summary>Gets or sets the assembly labour cost.</summary>
    public decimal LaborCost { get; set; }

    /// <summary>Gets or sets the total cost of one unit.</summary>
    public decimal TotalCost { get; set; }

    /// <summary>Gets or sets a value indicating whether every input was available on the requested date.</summary>
    public bool IsComplete { get; set; }
}

/// <summary>
/// Signed difference between a draft composition and the persisted one on the same date.
/// Labour is included so the client does not have to infer why it never moves.
/// </summary>
public class PreviewProductBomCostDelta
{
    /// <summary>Gets or sets the material cost change.</summary>
    public decimal MaterialDelta { get; set; }

    /// <summary>Gets or sets the logistics cost change.</summary>
    public decimal LogisticsDelta { get; set; }

    /// <summary>Gets or sets the packaging cost change.</summary>
    public decimal PackagingDelta { get; set; }

    /// <summary>Gets or sets the labour cost change, which stays zero while the output rate is unchanged.</summary>
    public decimal LaborDelta { get; set; }

    /// <summary>Gets or sets the total cost change.</summary>
    public decimal TotalDelta { get; set; }
}

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
