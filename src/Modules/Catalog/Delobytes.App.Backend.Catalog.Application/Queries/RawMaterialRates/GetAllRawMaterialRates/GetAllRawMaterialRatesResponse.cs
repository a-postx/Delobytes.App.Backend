using Delobytes.App.Backend.Catalog.Application.Queries.RawMaterialRates.GetRawMaterialRates;

namespace Delobytes.App.Backend.Catalog.Application.Queries.RawMaterialRates.GetAllRawMaterialRates;

public class GetAllRawMaterialRatesResponse
{
    public IReadOnlyList<RawMaterialRateItem> Items { get; set; } = new List<RawMaterialRateItem>();
}
