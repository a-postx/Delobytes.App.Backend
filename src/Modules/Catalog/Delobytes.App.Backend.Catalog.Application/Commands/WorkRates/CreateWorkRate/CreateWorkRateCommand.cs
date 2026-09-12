using Delobytes.App.Backend.Identity.Domain.Enums;
using Delobytes.App.Backend.Identity.Domain.Interfaces;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.WorkRates.CreateWorkRate;

public class CreateWorkRateCommand : IRequest<CreateWorkRateResponse>, IRequireRole
{
    public string Name { get; set; } = default!;

    public decimal DailyWage { get; set; }

    public DateOnly ValidFrom { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
