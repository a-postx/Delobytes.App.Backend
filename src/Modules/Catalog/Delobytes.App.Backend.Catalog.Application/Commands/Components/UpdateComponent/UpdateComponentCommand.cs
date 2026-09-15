using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Components.UpdateComponent;

/// <summary>
/// Updates descriptive fields only. Price and supplier are versioned through
/// <see cref="CreateComponentPrice.CreateComponentPriceCommand"/>.
/// </summary>
public class UpdateComponentCommand : IRequest<UpdateComponentResponse>, IRequireRole
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public Domain.Enums.Unit Unit { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
