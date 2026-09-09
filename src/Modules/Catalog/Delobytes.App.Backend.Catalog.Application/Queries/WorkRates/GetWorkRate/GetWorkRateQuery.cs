using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.WorkRates.GetWorkRate;

public class GetWorkRateQuery : IRequest<GetWorkRateResponse?>
{
    public Guid Id { get; set; }
}
