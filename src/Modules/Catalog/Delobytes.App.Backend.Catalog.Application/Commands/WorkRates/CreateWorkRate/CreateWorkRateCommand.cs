using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.WorkRates.CreateWorkRate;

public class CreateWorkRateCommand : IRequest<CreateWorkRateResponse>, IRequireRole
{
    public string Name { get; set; } = default!;

    public decimal DailyWage { get; set; }

    public DateOnly ValidFrom { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
