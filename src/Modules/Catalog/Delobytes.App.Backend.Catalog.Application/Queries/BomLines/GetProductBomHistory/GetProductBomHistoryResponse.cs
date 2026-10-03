using Delobytes.App.Backend.Catalog.Application.Queries.BomLines;
namespace Delobytes.App.Backend.Catalog.Application.Queries.BomLines.GetProductBomHistory;
public class GetProductBomHistoryResponse { public List<BomLineDto> Items { get; set; } = new List<BomLineDto>(); }