using Delobytes.App.Backend.Identity.Domain.Enums;
using Delobytes.App.Backend.Identity.Domain.Interfaces;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.RawMaterialRates.CreateRawMaterialRate;

public class CreateRawMaterialRateCommand : IRequest<CreateRawMaterialRateResponse>, IRequireRole
{
    public Guid ProductId { get; set; }

    public decimal CostPerUnit { get; set; }

    public DateOnly ValidFrom { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
