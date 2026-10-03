using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;
namespace Delobytes.App.Backend.Catalog.Application.Commands.BomLines.CreateBomLine;
public class CreateBomLineCommand : IRequest<CreateBomLineResponse>, IRequireRole
{
    public Guid ProductId { get; set; }
    public Guid ComponentId { get; set; }
    public decimal Quantity { get; set; }
    public DateOnly ValidFrom { get; set; }
    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}