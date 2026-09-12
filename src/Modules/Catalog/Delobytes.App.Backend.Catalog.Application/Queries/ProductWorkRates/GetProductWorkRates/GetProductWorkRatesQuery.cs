using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.ProductWorkRates.GetProductWorkRates;

public class GetProductWorkRatesQuery : IRequest<GetProductWorkRatesResponse>
{
    public Guid ProductId { get; set; }
}
