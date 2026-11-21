namespace Delobytes.App.Backend.Finance.Application.ReadModels;

/// <summary>
/// Money of one foreign currency that the report currency filter kept out of a P&amp;L view.
/// </summary>
/// <remarks>
/// Amounts of different currencies are never added up, so a report cannot show them in its totals.
/// It reports them here instead, so a reader can see that the reported period holds more than the
/// single-currency figures say.
/// </remarks>
public sealed class PnlCurrencyExclusion
{
    public string Currency { get; set; } = default!;

    /// <summary>Gets or sets the number of lines of this currency the filter excluded.</summary>
    public int Lines { get; set; }

    /// <summary>Gets or sets the revenue of those lines, in their own currency.</summary>
    public decimal RevenueAmount { get; set; }
}
