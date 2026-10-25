using MediatR;
using Delobytes.App.Backend.Identity.Application.Interfaces;

namespace Delobytes.App.Backend.Identity.Application.Queries;

/// <summary>
/// Query to retrieve tenant legal entity settings.
/// </summary>
public class GetTenantLegalEntityQuery : IRequest<GetTenantLegalEntityResponse>
{
    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }
}

/// <summary>
/// Response for GetTenantLegalEntityQuery.
/// </summary>
public class GetTenantLegalEntityResponse
{
    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the full legal name.
    /// </summary>
    public string? LegalName { get; set; }

    /// <summary>
    /// Gets or sets the taxpayer identification number (ИНН).
    /// </summary>
    public string? Inn { get; set; }
}

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
        };
    }
}
