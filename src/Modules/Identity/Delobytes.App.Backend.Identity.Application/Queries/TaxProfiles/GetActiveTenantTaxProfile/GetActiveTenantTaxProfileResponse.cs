using Delobytes.App.Backend.Contracts.Accounting;

namespace Delobytes.App.Backend.Identity.Application.Queries.TaxProfiles.GetActiveTenantTaxProfile;

/// <summary>
/// Ответ на запрос действующего налогового профиля.
/// </summary>
public class GetActiveTenantTaxProfileResponse
{
    /// <summary>
    /// Gets or sets признак наличия профиля на указанный момент.
    /// </summary>
    public bool Found { get; set; }

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
}
