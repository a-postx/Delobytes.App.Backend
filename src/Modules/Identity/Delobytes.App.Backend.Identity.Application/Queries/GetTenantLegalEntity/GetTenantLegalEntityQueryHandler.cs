using Delobytes.App.Backend.Identity.Application.Interfaces;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Queries.GetTenantLegalEntity;

/// <summary>
/// Handler for GetTenantLegalEntityQuery.
/// </summary>
public class GetTenantLegalEntityQueryHandler : IRequestHandler<GetTenantLegalEntityQuery, GetTenantLegalEntityResponse>
{
    private readonly ITenantRepository _tenantRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetTenantLegalEntityQueryHandler"/> class.
    /// </summary>
    public GetTenantLegalEntityQueryHandler(ITenantRepository tenantRepository)
    {
        _tenantRepository = tenantRepository;
    }

    /// <inheritdoc/>
    public async Task<GetTenantLegalEntityResponse> Handle(GetTenantLegalEntityQuery request, CancellationToken cancellationToken)
    {
        Domain.Entities.Tenant? tenant = await _tenantRepository.FindByIdAsync(request.TenantId, cancellationToken);

        if (tenant == null)
        {
            throw new InvalidOperationException($"Пространство с ID {request.TenantId} не найдено.");
        }

        return new GetTenantLegalEntityResponse
        {
            TenantId = tenant.Id,
            LegalName = tenant.LegalName,
            Inn = tenant.Inn,
            TaxType = tenant.TaxType,
            TaxRatePercent = tenant.TaxRatePercent,
            VatType = tenant.VatType,
        };
    }
}
