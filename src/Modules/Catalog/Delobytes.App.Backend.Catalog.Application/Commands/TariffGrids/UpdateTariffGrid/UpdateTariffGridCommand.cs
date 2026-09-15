using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.TariffGrids.UpdateTariffGrid;

public class UpdateTariffGridCommand : IRequest<UpdateTariffGridResponse>, IRequireRole
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public bool IsActive { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
