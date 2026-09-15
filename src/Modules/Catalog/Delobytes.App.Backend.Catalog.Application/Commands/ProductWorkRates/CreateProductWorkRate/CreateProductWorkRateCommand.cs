using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.ProductWorkRates.CreateProductWorkRate;

public class CreateProductWorkRateCommand : IRequest<CreateProductWorkRateResponse>, IRequireRole
{
    public Guid ProductId { get; set; }

    public Guid WorkRateId { get; set; }

    public int AssemblyRatePerDay { get; set; }

    public DateOnly ValidFrom { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
