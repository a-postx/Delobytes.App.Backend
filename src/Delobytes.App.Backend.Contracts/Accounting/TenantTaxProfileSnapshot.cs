namespace Delobytes.App.Backend.Contracts.Accounting;

/// <summary>
/// Снимок налогового профиля тенанта на определённую дату.
/// </summary>
public sealed class TenantTaxProfileSnapshot
{
    /// <summary>
    /// Gets or sets налоговый режим.
    /// </summary>
    public TaxRegime Regime { get; init; }

    /// <summary>
    /// Gets or sets ставка налога в процентах (0–100), не доля.
    /// </summary>
    public decimal RatePercent { get; init; }

    /// <summary>
    /// Gets or sets режим НДС.
    /// </summary>
    public VatType Vat { get; init; }

    /// <summary>
    /// Gets or sets дата, с которой действует профиль.
    /// </summary>
    public DateOnly ValidFrom { get; init; }
}
