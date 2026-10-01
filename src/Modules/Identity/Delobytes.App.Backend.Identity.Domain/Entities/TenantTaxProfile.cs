using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Identity.Domain.Entities;

/// <summary>
/// Версия налогового профиля тенанта. Записи неизменяемы: изменение оформляется
/// новой версией с более поздним <see cref="ValidFrom"/>, чтобы прошлые периоды
/// оставались воспроизводимыми.
/// </summary>
public class TenantTaxProfile : ITenantScoped
{
    /// <summary>
    /// Gets or sets идентификатор версии профиля.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets идентификатор тенанта.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets налоговый режим.
    /// </summary>
    public TaxRegime Regime { get; set; }

    /// <summary>
    /// Gets or sets ставка налога в процентах (0–100), не доля.
    /// </summary>
    public decimal RatePercent { get; set; }

    /// <summary>
    /// Gets or sets режим НДС.
    /// </summary>
    public VatType Vat { get; set; }

    /// <summary>
    /// Gets or sets дата, с которой действует эта версия профиля.
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
