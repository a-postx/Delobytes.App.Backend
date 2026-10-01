using Delobytes.App.Backend.Contracts.Accounting;

namespace Delobytes.App.Backend.Identity.Application.Queries.TaxProfiles.GetTenantTaxProfiles;

/// <summary>
/// Ответ на запрос списка налоговых профилей тенанта.
/// </summary>
public class GetTenantTaxProfilesResponse
{
    /// <summary>
    /// Gets or sets версии профиля, отсортированные по дате начала по убыванию.
    /// </summary>
    public List<TenantTaxProfileItem> Items { get; set; } = new List<TenantTaxProfileItem>();
}

/// <summary>
/// Версия налогового профиля в списке.
/// </summary>
public class TenantTaxProfileItem
{
    /// <summary>
    /// Gets or sets идентификатор версии профиля.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets налоговый режим.
    /// </summary>
    public TaxRegime Regime { get; set; }

    /// <summary>
    /// Gets or sets ставка налога в процентах.
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

    /// <summary>
    /// Gets or sets момент создания записи.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets идентификатор создателя записи.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }
}
