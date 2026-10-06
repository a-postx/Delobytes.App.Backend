using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.WorkRates.UpdateWorkRate;

/// <summary>
/// Updates descriptive fields only: name and activity status. The daily wage is versioned through
/// <see cref="CreateWorkRateVersion.CreateWorkRateVersionCommand"/>.
/// </summary>
public class UpdateWorkRateCommand : IRequest<UpdateWorkRateResponse>, IRequireRole
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public bool IsActive { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
