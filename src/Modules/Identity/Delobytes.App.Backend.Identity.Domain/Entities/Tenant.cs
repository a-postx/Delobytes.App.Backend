using Delobytes.App.Backend.Identity.Domain.Constants;

namespace Delobytes.App.Backend.Identity.Domain.Entities;

/// <summary>
/// Represents a tenant (organization/workspace) in the multi-tenant system.
/// </summary>
public class Tenant
{
    /// <summary>
    /// Gets or sets the tenant unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the tenant name (required).
    /// </summary>
    public string Name { get; set; } = default!;

    /// <summary>
    /// Gets or sets the date and time when the tenant was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the tenant was last updated.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the tenant is active.
    /// </summary>
    public bool IsActive { get; set; }

    // ── Legal entity details ───────────────────────────────────────────────────

    /// <summary>Full legal name (e.g., ООО "Ромашка").</summary>
    public string? LegalName { get; set; }

    /// <summary>Short legal name (e.g., ООО "Ромашка").</summary>
    public string? LegalNameShort { get; set; }

    /// <summary>Taxpayer identification number (ИНН).</summary>
    public string? Inn { get; set; }

    /// <summary>Tax registration reason code (КПП).</summary>
    public string? Kpp { get; set; }

    /// <summary>Primary state registration number (ОГРН).</summary>
    public string? Ogrn { get; set; }

    /// <summary>Legal address.</summary>
    public string? LegalAddress { get; set; }

    /// <summary>Actual/postal address.</summary>
    public string? ActualAddress { get; set; }

    /// <summary>Director name (or CEO).</summary>
    public string? DirectorName { get; set; }

    /// <summary>Contact phone.</summary>
    public string? Phone { get; set; }

    /// <summary>Contact email.</summary>
    public string? Email { get; set; }

    /// <summary>Bank account number (расчётный счёт).</summary>
    public string? BankAccount { get; set; }

    /// <summary>Bank name.</summary>
    public string? BankName { get; set; }

    /// <summary>Bank identification code (БИК).</summary>
    public string? Bik { get; set; }

    /// <summary>Correspondent account (корреспондентский счёт).</summary>
    public string? CorrespondentAccount { get; set; }

    // ── Business settings ──────────────────────────────────────────────────────

    /// <summary>Primary business currency (ISO 4217 code).</summary>
    public string Currency { get; set; } = TenantCurrencies.Rub;

    /// <summary>Timezone ID (e.g., "Europe/Moscow").</summary>
    public string TimeZone { get; set; } = "Europe/Moscow";

    /// <summary>
    /// Navigation property: tenant memberships.
    /// </summary>
    public ICollection<TenantMembership> Memberships { get; set; } = new List<TenantMembership>();
}
