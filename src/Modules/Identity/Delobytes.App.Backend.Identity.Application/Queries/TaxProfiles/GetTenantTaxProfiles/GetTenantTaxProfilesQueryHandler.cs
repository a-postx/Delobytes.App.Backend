using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Queries.TaxProfiles.GetTenantTaxProfiles;

/// <summary>
/// Handler for GetTenantTaxProfilesQuery.
/// </summary>
public class GetTenantTaxProfilesQueryHandler : IRequestHandler<GetTenantTaxProfilesQuery, GetTenantTaxProfilesResponse>
{
    private readonly ITenantTaxProfileRepository _taxProfileRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetTenantTaxProfilesQueryHandler"/> class.
    /// </summary>
    public GetTenantTaxProfilesQueryHandler(ITenantTaxProfileRepository taxProfileRepository)
    {
        _taxProfileRepository = taxProfileRepository;
    }

    /// <inheritdoc/>
    public async Task<GetTenantTaxProfilesResponse> Handle(
        GetTenantTaxProfilesQuery request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TenantTaxProfile> profiles = await _taxProfileRepository.GetByTenantAsync(request.TenantId, cancellationToken);

        return new GetTenantTaxProfilesResponse
        {
            Items = profiles.Select(p => new TenantTaxProfileItem
            {
                Id = p.Id,
                Regime = p.Regime,
                RatePercent = p.RatePercent,
                Vat = p.Vat,
                ValidFrom = p.ValidFrom,
                CreatedAt = p.CreatedAt,
                CreatedByUserId = p.CreatedByUserId,
            }).ToList(),
        };
    }
}
