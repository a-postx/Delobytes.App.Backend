using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.TariffGrids.GetTariffGrid;

public class GetTariffGridQuery : IRequest<GetTariffGridResponse?>
{
    public Guid Id { get; set; }
}
