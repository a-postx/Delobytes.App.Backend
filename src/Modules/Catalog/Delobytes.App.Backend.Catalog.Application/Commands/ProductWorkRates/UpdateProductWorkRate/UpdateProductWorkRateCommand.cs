using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.ProductWorkRates.UpdateProductWorkRate;

/// <summary>
/// Corrects an existing, still-active work rate version in place (e.g. a typo in the rate or
/// the date), as opposed to CreateProductWorkRateCommand, which appends a new version.
/// </summary>
public class UpdateProductWorkRateCommand : IRequest<UpdateProductWorkRateResponse>, IRequireRole
{
    public Guid Id { get; set; }

    public Guid WorkRateId { get; set; }

    public int AssemblyRatePerDay { get; set; }

    public DateOnly ValidFrom { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
