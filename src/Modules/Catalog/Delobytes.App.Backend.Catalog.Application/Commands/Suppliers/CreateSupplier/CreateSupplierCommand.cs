using Delobytes.App.Backend.Identity.Domain.Enums;
using Delobytes.App.Backend.Identity.Domain.Interfaces;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Suppliers.CreateSupplier;

public class CreateSupplierCommand : IRequest<CreateSupplierResponse>, IRequireRole
{
    public string Name { get; set; } = default!;

    public string? ContactInfo { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
