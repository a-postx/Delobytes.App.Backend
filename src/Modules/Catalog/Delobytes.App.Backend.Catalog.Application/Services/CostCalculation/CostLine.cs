using Delobytes.App.Backend.Catalog.Domain.Enums;

namespace Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;

/// <summary>
/// One BOM position as it contributes to the product cost.
/// <see cref="LineTotal"/> is zero when no price was effective on the requested date, but the line
/// is still reported so the UI can show which position could not be priced.
/// </summary>
/// <param name="ComponentId">Component identifier.</param>
/// <param name="ComponentName">Component display name.</param>
/// <param name="Category">Category that decides which cost bucket the line falls into.</param>
/// <param name="Quantity">Component quantity per one unit of the product.</param>
/// <param name="PricePerUnit">Price effective on the requested date; zero when no price exists.</param>
/// <param name="LineTotal">Quantity multiplied by price.</param>
public sealed record CostLine(
    Guid ComponentId,
    string ComponentName,
    ComponentCategory Category,
    decimal Quantity,
    decimal PricePerUnit,
    decimal LineTotal);
