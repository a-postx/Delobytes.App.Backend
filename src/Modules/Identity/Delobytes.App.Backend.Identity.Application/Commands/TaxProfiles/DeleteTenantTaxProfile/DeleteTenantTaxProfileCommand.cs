using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.TaxProfiles.DeleteTenantTaxProfile;

/// <summary>
/// Команда удаления версии налогового профиля тенанта.
/// </summary>
public class DeleteTenantTaxProfileCommand : IRequest<DeleteTenantTaxProfileResponse>, IRequireRole
{
    /// <summary>
    /// Gets or sets идентификатор тенанта.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets идентификатор удаляемой версии профиля.
    /// </summary>
    public Guid Id { get; set; }

    /// <inheritdoc/>
    public Role[] AllowedRoles => new[] { Role.Administrator };
}
