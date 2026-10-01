namespace Delobytes.App.Backend.Identity.Application.Queries.GetTenantLegalEntity;

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
