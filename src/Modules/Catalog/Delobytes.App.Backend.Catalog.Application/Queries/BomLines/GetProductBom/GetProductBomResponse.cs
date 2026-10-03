using Delobytes.App.Backend.Catalog.Application.Queries.BomLines;
namespace Delobytes.App.Backend.Catalog.Application.Queries.BomLines.GetProductBom;
public class GetProductBomResponse { public List<BomLineDto> Items { get; set; } = new List<BomLineDto>(); }