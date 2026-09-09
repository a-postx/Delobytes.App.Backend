using Delobytes.App.Backend.Catalog.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.TariffGrids.GetTariffGrids;

public class GetTariffGridsQuery : IRequest<GetTariffGridsResponse>
{
    public TariffType? TariffType { get; set; }
}
