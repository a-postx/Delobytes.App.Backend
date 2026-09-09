using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Identity.Domain.Enums;
using Delobytes.App.Backend.Identity.Domain.Interfaces;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.TariffGrids.CreateTariffGrid;

public class TariffGridEntryRequest
{
    public string RegionOrCity { get; set; } = default!;

    public decimal? VolumeThresholdLiters { get; set; }

    public decimal Rate { get; set; }
}

public class CreateTariffGridCommand : IRequest<CreateTariffGridResponse>, IRequireRole
{
    public string Name { get; set; } = default!;

    public TariffType TariffType { get; set; }

    public DateOnly ValidFrom { get; set; }

    public Guid? ChannelId { get; set; }

    public IList<TariffGridEntryRequest> Entries { get; set; } = new List<TariffGridEntryRequest>();

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
