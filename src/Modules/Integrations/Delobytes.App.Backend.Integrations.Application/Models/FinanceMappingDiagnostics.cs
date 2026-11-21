namespace Delobytes.App.Backend.Integrations.Application.Models;

/// <summary>
/// Counters collected while a finance document is mapped. Every skipped row or fallback is visible
/// here rather than in a log line, because the point of the finance import is to make missing money
/// measurable.
/// </summary>
public sealed class FinanceMappingDiagnostics
{
    /// <summary>Gets or sets rows skipped because the operation is out of scope.</summary>
    public int SkippedOperations { get; set; }

    /// <summary>Gets or sets rows skipped because they carry no unit identity.</summary>
    public int RowsWithoutSrid { get; set; }

    /// <summary>Gets or sets rows whose document date had to fall back to the sale date.</summary>
    public int RowsWithoutRrDate { get; set; }

    /// <summary>Gets or sets accruals emitted without a control total.</summary>
    public int AccrualsWithoutControlTotal { get; set; }

    /// <summary>Gets or sets reversals recognised in the document.</summary>
    public int Reversals { get; set; }

    /// <summary>Gets or sets buyer returns recognised in the document.</summary>
    public int Returns { get; set; }

    /// <summary>Gets or sets accruals where the sale amount identity did not hold.</summary>
    public int SaleAmountMismatches { get; set; }

    /// <summary>Gets or sets accruals where the commission had to fall back to the sale commission.</summary>
    public int CommissionFallbacks { get; set; }

    /// <summary>
    /// Gets or sets sale rows where the seller-price split did not reproduce the reported payout, so
    /// the row was booked as the channel's composite instead.
    /// </summary>
    public int SplitFallbacks { get; set; }


    /// <summary>Gets or sets appended entries that matched no order line yet.</summary>
    public int Unattributed { get; set; }

    /// <summary>Gets or sets rows of a new report instance that matched no current entry.</summary>
    public int UnmatchedInstanceRows { get; set; }

    /// <summary>Adds another page's counters into this instance.</summary>
    /// <param name="other">Counters to add.</param>
    public void Add(FinanceMappingDiagnostics other)
    {
        ArgumentNullException.ThrowIfNull(other);

        SkippedOperations += other.SkippedOperations;
        RowsWithoutSrid += other.RowsWithoutSrid;
        RowsWithoutRrDate += other.RowsWithoutRrDate;
        AccrualsWithoutControlTotal += other.AccrualsWithoutControlTotal;
        Reversals += other.Reversals;
        Returns += other.Returns;
        SaleAmountMismatches += other.SaleAmountMismatches;
        CommissionFallbacks += other.CommissionFallbacks;
        SplitFallbacks += other.SplitFallbacks;
        Unattributed += other.Unattributed;
        UnmatchedInstanceRows += other.UnmatchedInstanceRows;
    }
}

