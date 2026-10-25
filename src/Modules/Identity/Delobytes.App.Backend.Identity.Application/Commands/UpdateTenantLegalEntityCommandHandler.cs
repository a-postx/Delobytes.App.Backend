using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;
using Delobytes.App.Backend.Identity.Application.Interfaces;

namespace Delobytes.App.Backend.Identity.Application.Commands;

/// <summary>
/// Command to update tenant legal entity settings.
/// </summary>
public class UpdateTenantLegalEntityCommand : IRequest<UpdateTenantLegalEntityResponse>, IRequireRole
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

    /// <inheritdoc/>
    public Role[] AllowedRoles => new[] { Role.Administrator };
}

/// <summary>
/// Response for UpdateTenantLegalEntityCommand.
/// </summary>
public class UpdateTenantLegalEntityResponse
{
    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the updated legal name.
    /// </summary>
    public string? LegalName { get; set; }

    /// <summary>
    /// Gets or sets the updated INN.
    /// </summary>
    public string? Inn { get; set; }
}

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
        tenant.UpdatedAt = DateTimeOffset.UtcNow;

        _tenantRepository.Update(tenant);
        await _tenantRepository.SaveChangesAsync(cancellationToken);

        return new UpdateTenantLegalEntityResponse
        {
            TenantId = tenant.Id,
            LegalName = tenant.LegalName,
            Inn = tenant.Inn,
        };
    }
}
