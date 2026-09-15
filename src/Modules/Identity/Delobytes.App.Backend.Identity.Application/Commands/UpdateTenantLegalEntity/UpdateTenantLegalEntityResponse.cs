using Delobytes.App.Backend.Identity.Domain.Enums;

namespace Delobytes.App.Backend.Identity.Application.Commands.UpdateTenantLegalEntity;

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

    /// <summary>
    /// Gets or sets the updated tax type.
    /// </summary>
    public TaxType TaxType { get; set; }

    /// <summary>
    /// Gets or sets the updated tax rate.
    /// </summary>
    public decimal TaxRatePercent { get; set; }

    /// <summary>
    /// Gets or sets the updated VAT type.
    /// </summary>
    public VatType VatType { get; set; }
}
