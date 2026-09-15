using Delobytes.App.Backend.Identity.Application.Interfaces;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.UpdateTenantLegalEntity;

/// <summary>
/// Handler for UpdateTenantLegalEntityCommand.
/// </summary>
public class UpdateTenantLegalEntityCommandHandler : IRequestHandler<UpdateTenantLegalEntityCommand, UpdateTenantLegalEntityResponse>
{
    private readonly ITenantRepository _tenantRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateTenantLegalEntityCommandHandler"/> class.
    /// </summary>
    public UpdateTenantLegalEntityCommandHandler(ITenantRepository tenantRepository)
    {
        _tenantRepository = tenantRepository;
    }

    /// <inheritdoc/>
    public async Task<UpdateTenantLegalEntityResponse> Handle(UpdateTenantLegalEntityCommand request, CancellationToken cancellationToken)
    {
        Domain.Entities.Tenant? tenant = await _tenantRepository.FindByIdAsync(request.TenantId, cancellationToken);

        if (tenant == null)
        {
            throw new InvalidOperationException($"Пространство с ID {request.TenantId} не найдено.");
        }

        tenant.LegalName = request.LegalName;
        tenant.Inn = request.Inn;
        tenant.TaxType = request.TaxType;
        tenant.TaxRatePercent = request.TaxRatePercent;
        tenant.VatType = request.VatType;
        tenant.UpdatedAt = DateTimeOffset.UtcNow;

        _tenantRepository.Update(tenant);
        await _tenantRepository.SaveChangesAsync(cancellationToken);

        return new UpdateTenantLegalEntityResponse
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
