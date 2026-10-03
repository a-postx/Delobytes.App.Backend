using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;
namespace Delobytes.App.Backend.Catalog.Application.Commands.BomLines.DeleteBomLine;
public class DeleteBomLineCommand : IRequest<DeleteBomLineResponse>, IRequireRole { public Guid Id { get; set; } public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator }; }