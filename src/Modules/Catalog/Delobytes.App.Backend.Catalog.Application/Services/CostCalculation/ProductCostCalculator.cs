using System.Globalization;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;

namespace Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;

/// <summary>
/// Read-only product cost calculation.
/// Every input is resolved through a date-aware repository lookup, so a calculation repeated for a
/// past date returns the same figures even after prices, wages or the BOM have moved on.
/// </summary>
public class ProductCostCalculator : ICostCalculator
{
    private readonly IBomLineRepository _bomLineRepository;
    private readonly IComponentPriceRepository _componentPriceRepository;
    private readonly IProductWorkRateRepository _productWorkRateRepository;
    private readonly IWorkRateRepository _workRateRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductCostCalculator"/> class.
    /// </summary>
    /// <param name="bomLineRepository">Source of the product composition version.</param>
    /// <param name="componentPriceRepository">Source of component prices.</param>
    /// <param name="productWorkRateRepository">Source of the product assembly output rate.</param>
    /// <param name="workRateRepository">Source of worker daily wages.</param>
    public ProductCostCalculator(
        IBomLineRepository bomLineRepository,
        IComponentPriceRepository componentPriceRepository,
        IProductWorkRateRepository productWorkRateRepository,
        IWorkRateRepository workRateRepository)
    {
        _bomLineRepository = bomLineRepository;
        _componentPriceRepository = componentPriceRepository;
        _productWorkRateRepository = productWorkRateRepository;
        _workRateRepository = workRateRepository;
    }

    /// <inheritdoc/>
    public async Task<CostBreakdown> CalculateAsync(Guid productId, DateOnly asOf, CancellationToken ct)
    {
        List<CostWarning> warnings = new List<CostWarning>();

        IReadOnlyList<BomLine> bomLines = await _bomLineRepository.GetEffectiveAtAsync(productId, asOf, ct);

        // An empty result means the product had no composition at all on that date, which is
        // different from a composition whose positions could not be priced. Both are zero totals,
        // but only the first one hides every position from the user.
        if (bomLines.Count == 0)
        {
            warnings.Add(new CostWarning(
                CostWarningType.MissingBom,
                string.Format(CultureInfo.InvariantCulture, "Состав товара не определён на дату {0}.", asOf),
                null));

            return Build(productId, asOf, 0m, 0m, 0m, 0m, new List<CostLine>(), warnings);
        }

        decimal materialCost = 0m;
        decimal logisticsCost = 0m;
        decimal packagingCost = 0m;

        List<CostLine> lines = new List<CostLine>(bomLines.Count);

        foreach (BomLine bomLine in bomLines)
        {
            Component component = bomLine.Component;
            string componentName = component.Name;

            if (bomLine.Quantity <= 0m)
            {
                // Historical rows predate the Etap 1 validation. The amount is unusable, but it must
                // not take the whole product down: report it and keep the other positions.
                warnings.Add(new CostWarning(
                    CostWarningType.InvalidQuantity,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Некорректное количество компонента {0}: {1}.",
                        componentName,
                        bomLine.Quantity),
                    bomLine.ComponentId));
            }

            ComponentPrice? componentPrice = await _componentPriceRepository
                .GetEffectiveAtAsync(bomLine.ComponentId, asOf, ct);

            if (componentPrice == null)
            {
                warnings.Add(new CostWarning(
                    CostWarningType.MissingComponentPrice,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Нет цены на компонент {0} на дату {1}.",
                        componentName,
                        asOf),
                    bomLine.ComponentId));
            }

            // A missing price yields a zero-priced line rather than an omitted one, so the user sees
            // that the position exists but was not counted.
            decimal pricePerUnit = componentPrice == null ? 0m : componentPrice.PricePerUnit;
            decimal lineTotal = bomLine.Quantity * pricePerUnit;

            lines.Add(new CostLine(
                bomLine.ComponentId,
                componentName,
                component.Category,
                bomLine.Quantity,
                pricePerUnit,
                lineTotal));

            switch (component.Category)
            {
                case ComponentCategory.Material:
                    materialCost += lineTotal;
                    break;
                case ComponentCategory.Logistics:
                    logisticsCost += lineTotal;
                    break;
                case ComponentCategory.Packaging:
                    packagingCost += lineTotal;
                    break;
                default:
                    throw new InvalidOperationException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Unknown component category {0}.",
                            component.Category));
            }
        }

        decimal laborCost = await CalculateLaborCostAsync(productId, asOf, warnings, ct);

        return Build(productId, asOf, materialCost, logisticsCost, packagingCost, laborCost, lines, warnings);
    }

    private static CostBreakdown Build(
        Guid productId,
        DateOnly asOf,
        decimal materialCost,
        decimal logisticsCost,
        decimal packagingCost,
        decimal laborCost,
        IReadOnlyList<CostLine> lines,
        IReadOnlyList<CostWarning> warnings)
    {
        // Labour is charged as one worker's full daily wage divided by the number of units that
        // worker assembles in a day, which is what ProductWorkRate.AssemblyRatePerDay expresses.
        return new CostBreakdown(
            productId,
            asOf,
            materialCost,
            logisticsCost,
            packagingCost,
            laborCost,
            materialCost + logisticsCost + packagingCost + laborCost,
            lines,
            warnings);
    }

    private async Task<decimal> CalculateLaborCostAsync(
        Guid productId,
        DateOnly asOf,
        List<CostWarning> warnings,
        CancellationToken ct)
    {
        ProductWorkRate? productWorkRate = await _productWorkRateRepository
            .GetEffectiveAtAsync(productId, asOf, ct);

        if (productWorkRate == null)
        {
            warnings.Add(new CostWarning(
                CostWarningType.MissingWorkRate,
                string.Format(CultureInfo.InvariantCulture, "Норма выработки не задана на дату {0}.", asOf),
                null));

            return 0m;
        }

        // The rate may have been physically removed, which soft-delete should prevent but the
        // calculation still has to survive.
        WorkRate? workRate = await _workRateRepository.GetEffectiveAtAsync(productWorkRate.WorkRateId, asOf, ct);

        if (workRate == null)
        {
            warnings.Add(new CostWarning(
                CostWarningType.MissingWorkRate,
                string.Format(CultureInfo.InvariantCulture, "Ставка работы не найдена на дату {0}.", asOf),
                null));

            return 0m;
        }

        if (productWorkRate.AssemblyRatePerDay <= 0)
        {
            // Division by zero would abort the whole calculation and hide the material costs,
            // which are still valid and useful on their own.
            warnings.Add(new CostWarning(
                CostWarningType.InvalidQuantity,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Норма выработки равна нулю или отрицательна на дату {0}.",
                    asOf),
                null));

            return 0m;
        }

        return workRate.DailyWage / productWorkRate.AssemblyRatePerDay;
    }
}
