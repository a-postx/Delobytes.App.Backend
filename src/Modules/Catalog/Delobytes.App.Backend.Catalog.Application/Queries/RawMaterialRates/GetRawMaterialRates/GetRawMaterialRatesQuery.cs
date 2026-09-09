using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.RawMaterialRates.GetRawMaterialRates;

public class GetRawMaterialRatesQuery : IRequest<GetRawMaterialRatesResponse>
{
    public Guid ProductId { get; set; }
}
