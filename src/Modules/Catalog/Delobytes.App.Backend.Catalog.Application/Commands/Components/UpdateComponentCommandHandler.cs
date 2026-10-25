using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Components;

/// <summary>
/// Updates descriptive fields only: name, description, unit and category. Price and supplier are versioned through
/// <see cref="CreateComponentPriceCommand"/>.
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

public class UpdateComponentResponse
{
    public bool Found { get; set; }
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
        Component? component = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (component == null)
        {
            return new UpdateComponentResponse { Found = false };
        }

        component.Name = request.Name;
        component.Description = request.Description;
        component.Unit = request.Unit;
        component.Category = request.Category;

        await _repository.SaveChangesAsync(cancellationToken);

        return new UpdateComponentResponse { Found = true };
    }
}
