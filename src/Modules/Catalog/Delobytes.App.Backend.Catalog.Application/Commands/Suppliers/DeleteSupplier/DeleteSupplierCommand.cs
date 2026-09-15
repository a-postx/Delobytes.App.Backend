using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Suppliers.DeleteSupplier;

public class DeleteSupplierCommand : IRequest<DeleteSupplierResponse>, IRequireRole
{
    public Guid Id { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
