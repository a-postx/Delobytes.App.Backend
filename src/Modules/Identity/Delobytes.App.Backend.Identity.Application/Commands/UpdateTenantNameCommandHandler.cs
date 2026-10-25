using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands;

/// <summary>
/// Command to update tenant name.
/// </summary>
public class UpdateTenantNameCommand : IRequest<UpdateTenantNameResponse>, IRequireRole
{
    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the new tenant name.
    /// </summary>
    public string Name { get; set; } = default!;

    /// <inheritdoc/>
    public Role[] AllowedRoles => new[] { Role.Administrator };
}

/// <summary>
/// Response for UpdateTenantNameCommand.
/// </summary>
public class UpdateTenantNameResponse
{
    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the updated tenant name.
    /// </summary>
    public string Name { get; set; } = default!;
}

/// <summary>
/// Handler for UpdateTenantNameCommand.
/// </summary>
public class UpdateTenantNameCommandHandler : IRequestHandler<UpdateTenantNameCommand, UpdateTenantNameResponse>
{
    private readonly ITenantRepository _tenantRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateTenantNameCommandHandler"/> class.
    /// </summary>
    public UpdateTenantNameCommandHandler(ITenantRepository tenantRepository)
    {
        _tenantRepository = tenantRepository;
    }

    /// <inheritdoc/>
    public async Task<UpdateTenantNameResponse> Handle(UpdateTenantNameCommand request, CancellationToken cancellationToken)
    {
        Domain.Entities.Tenant? tenant = await _tenantRepository.FindByIdAsync(request.TenantId, cancellationToken);

        if (tenant == null)
        {
            throw new InvalidOperationException($"Пространство с ID {request.TenantId} не найдено.");
        }

        tenant.Name = request.Name;
        tenant.UpdatedAt = DateTimeOffset.UtcNow;

        _tenantRepository.Update(tenant);
        await _tenantRepository.SaveChangesAsync(cancellationToken);

        return new UpdateTenantNameResponse
        {
            TenantId = tenant.Id,
            Name = tenant.Name,
        };
    }
}
