using MediatR;
namespace Delobytes.App.Backend.Catalog.Application.Queries.BomLines.GetProductBom;
public class GetProductBomQuery : IRequest<GetProductBomResponse> { public Guid ProductId { get; set; } }