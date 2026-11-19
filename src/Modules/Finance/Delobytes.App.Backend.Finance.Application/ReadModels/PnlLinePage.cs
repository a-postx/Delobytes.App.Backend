namespace Delobytes.App.Backend.Finance.Application.ReadModels;

/// <summary>
/// One keyset page of P&amp;L lines.
/// </summary>
public sealed class PnlLinePage
{
    public IReadOnlyList<PnlLineRow> Rows { get; set; } = Array.Empty<PnlLineRow>();

    public string? NextToken { get; set; }

    /// <summary>Gets or sets how many lines the currency filter excluded.</summary>
    public int ExcludedByCurrency { get; set; }
}
