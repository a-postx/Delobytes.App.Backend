using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.TaxProfiles.CreateTenantTaxProfile;

/// <summary>
/// Команда создания новой версии налогового профиля тенанта.
/// </summary>
public class CreateTenantTaxProfileCommand : IRequest<CreateTenantTaxProfileResponse>, IRequireRole
{
    /// <summary>
    /// Gets or sets идентификатор тенанта.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets налоговый режим.
    /// </summary>
    public TaxRegime Regime { get; set; }

    /// <summary>
    /// Gets or sets ставка налога в процентах (0–100).
    /// </summary>
    public decimal RatePercent { get; set; }

    /// <summary>
    /// Gets or sets режим НДС.
    /// </summary>
    public VatType Vat { get; set; }

    /// <summary>
    /// Gets or sets дата начала действия версии.
    /// </summary>
    public DateOnly ValidFrom { get; set; }

    /// <inheritdoc/>
    public Role[] AllowedRoles => new[] { Role.Administrator };
}
