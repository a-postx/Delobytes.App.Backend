using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Contracts.Errors;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Components;

/// <summary>
/// Updates descriptive fields: name, description, unit and category. Price and supplier are versioned through
/// <see cref="CreateComponentPriceCommand"/>. The unit is editable only while the component has no price version.
/// Changing the category does not rewrite already captured snapshots; it moves the component's contribution
/// to another cost bucket in subsequent cost calculations.
/// </summary>
public class UpdateComponentCommand : IRequest<UpdateComponentResponse>, IRequireRole
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    /// <summary>
    /// Unit of the component. Accepted only while the component has no price versions, because
    /// saved prices and BOM quantities are expressed in it.
    /// </summary>
    public Domain.Enums.Unit Unit { get; set; }

    /// <summary>Категория компонента. По умолчанию — материал.</summary>
    public ComponentCategory Category { get; set; } = ComponentCategory.Material;

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}

public class UpdateComponentResponse
{
}

public class UpdateComponentCommandHandler : IRequestHandler<UpdateComponentCommand, UpdateComponentResponse>
{
    private readonly IComponentRepository _repository;

    public UpdateComponentCommandHandler(IComponentRepository repository)
    {
        _repository = repository;
    }

    public async Task<UpdateComponentResponse> Handle(UpdateComponentCommand request, CancellationToken cancellationToken)
    {
        Component? component = await _repository.GetWithPricesByIdAsync(request.Id, cancellationToken);

        if (component == null)
        {
            throw new AppException(CatalogErrorCodes.ComponentNotFound);
        }

        // The unit is fixed once the component is priced: a price version states how many units its
        // amount covers, and BOM lines state quantities in the same unit. Changing it would silently
        // reinterpret history rather than start a new one.
        if (component.Unit != request.Unit && component.Prices.Count > 0)
        {
            throw new AppException(CatalogErrorCodes.ComponentUnitImmutable);
        }

        component.Name = request.Name;
        component.Description = request.Description;
        component.Unit = request.Unit;
        component.Category = request.Category;

        await _repository.SaveChangesAsync(cancellationToken);

        return new UpdateComponentResponse();
    }
}
