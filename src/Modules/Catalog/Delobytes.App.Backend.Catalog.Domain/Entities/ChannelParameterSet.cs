using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// A versioned set of commercial parameters for one sales channel.
/// Editing any parameter creates a new record with the new ValidFrom date;
/// prior records are kept unchanged so historical snapshots remain correct.
/// </summary>
public class ChannelParameterSet : ITenantScoped
{
    public Guid Id { get; set; }

    public Guid ChannelId { get; set; }

    /// <summary>Marketplace commission, as a fraction (e.g. 0.15 = 15%).</summary>
    public decimal CommissionPercent { get; set; }

    /// <summary>Payment acquiring fee, as a fraction.</summary>
    public decimal AcquiringPercent { get; set; }

    /// <summary>Self-promotion programme (СПП) discount, as a fraction.</summary>
    public decimal SppPercent { get; set; }

    /// <summary>Whether the SPP discount is applied when calculating buyer price.</summary>
    public bool SppEnabled { get; set; }

    public TaxType TaxType { get; set; }

    /// <summary>Applicable tax rate, as a fraction (e.g. 0.06 for simplified USN at 6%).</summary>
    public decimal TaxRatePercent { get; set; }

    /// <summary>Date from which this parameter set becomes effective.</summary>
    public DateOnly ValidFrom { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Channel Channel { get; set; } = default!;

    public ICollection<ProductChannelInput> ProductChannelInputs { get; set; } = new List<ProductChannelInput>();
}
