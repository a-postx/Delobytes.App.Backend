using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Components.UpdateComponent;

/// <summary>
/// Updates descriptive fields only: name, description, unit and category. Price and supplier are versioned through
/// <see cref="CreateComponentPrice.CreateComponentPriceCommand"/>.
/// Changing the category does not rewrite already captured snapshots; it moves the component's contribution
/// to another cost bucket in subsequent cost calculations.
/// </summary>
public class UpdateComponentCommand : IRequest<UpdateComponentResponse>, IRequireRole
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public Domain.Enums.Unit Unit { get; set; }

    /// <summary>Категория компонента. По умолчанию — материал.</summary>
    public ComponentCategory Category { get; set; } = ComponentCategory.Material;

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
