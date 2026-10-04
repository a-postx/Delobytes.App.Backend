using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;
namespace Delobytes.App.Backend.Catalog.Application.Commands.BomLines.UpsertProductBom;

public class UpsertProductBomCommand : IRequest<UpsertProductBomResponse>, IRequireRole
{
    public Guid ProductId { get; set; }
    public List<UpsertProductBomItem> Lines { get; set; } = new List<UpsertProductBomItem>();
    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}

public class UpsertProductBomItem
{
    public Guid ComponentId { get; set; }
    public decimal Quantity { get; set; }
}
