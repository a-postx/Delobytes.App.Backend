using Delobytes.App.Backend.Identity.Domain.Enums;
using Delobytes.App.Backend.Identity.Domain.Interfaces;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.WorkRates.UpdateWorkRate;

public class UpdateWorkRateCommand : IRequest<UpdateWorkRateResponse>, IRequireRole
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public decimal DailyWage { get; set; }

    public int AssemblyRatePerDay { get; set; }

    public DateOnly ValidFrom { get; set; }

    public bool IsActive { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
