using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Identity.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.UpdateTenantLegalEntity;

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

    /// <summary>
    /// Gets or sets the tax regime (УСН, ОСНО).
    /// </summary>
    public TaxType TaxType { get; set; }

    /// <summary>
    /// Gets or sets the tax rate as a fraction (e.g. 0.06 for 6%).
    /// </summary>
    public decimal TaxRatePercent { get; set; }

    /// <summary>
    /// Gets or sets the VAT type.
    /// </summary>
    public VatType VatType { get; set; }

    /// <inheritdoc/>
    public Role[] AllowedRoles => new[] { Role.Administrator };
}
