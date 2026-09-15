using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.ProductWorkRates.DeleteProductWorkRate;

public class DeleteProductWorkRateCommand : IRequest<DeleteProductWorkRateResponse>, IRequireRole
{
    public Guid Id { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
