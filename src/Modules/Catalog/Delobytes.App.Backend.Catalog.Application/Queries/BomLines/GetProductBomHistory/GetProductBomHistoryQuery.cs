using MediatR;
namespace Delobytes.App.Backend.Catalog.Application.Queries.BomLines.GetProductBomHistory;
public class GetProductBomHistoryQuery : IRequest<GetProductBomHistoryResponse> { public Guid ProductId { get; set; } }