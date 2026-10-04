using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;

/// <summary>
/// One composition position handed to the calculator, independent of where the line came from.
/// Used both for the persisted composition and for a draft that has not been saved yet, so the
/// calculator never has to know which of the two it is pricing.
/// </summary>
/// <param name="ComponentId">Component identifier.</param>
/// <param name="Component">Resolved component, source of the name and the category.</param>
/// <param name="Quantity">Component quantity per one unit of the product.</param>
public sealed record CostCalculationLine(Guid ComponentId, Component Component, decimal Quantity);
