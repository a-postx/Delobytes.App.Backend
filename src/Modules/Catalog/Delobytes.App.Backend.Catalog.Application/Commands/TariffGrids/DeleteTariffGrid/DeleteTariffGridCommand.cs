using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.TariffGrids.DeleteTariffGrid;

public class DeleteTariffGridCommand : IRequest<DeleteTariffGridResponse>, IRequireRole
{
    public Guid Id { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
