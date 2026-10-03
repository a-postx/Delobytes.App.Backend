namespace Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;

/// <summary>
/// A single reason the calculated cost cannot be treated as complete.
/// </summary>
/// <param name="Type">Machine-readable warning kind.</param>
/// <param name="Message">Human-readable description shown to the user.</param>
/// <param name="ComponentId">Component the warning belongs to, or null when it concerns the whole product.</param>
public sealed record CostWarning(CostWarningType Type, string Message, Guid? ComponentId);
