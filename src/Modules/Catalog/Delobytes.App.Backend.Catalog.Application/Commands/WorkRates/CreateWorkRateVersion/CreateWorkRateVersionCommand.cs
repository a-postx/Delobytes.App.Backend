using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.WorkRates.CreateWorkRateVersion;

/// <summary>
/// Adds a new daily wage version for an existing work rate. The previous active
/// version is deactivated; no existing wage record is ever overwritten.
/// </summary>
public class CreateWorkRateVersionCommand : IRequest<CreateWorkRateVersionResponse>, IRequireRole
{
    public Guid WorkRateId { get; set; }

    public decimal DailyWage { get; set; }

    public DateOnly ValidFrom { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
