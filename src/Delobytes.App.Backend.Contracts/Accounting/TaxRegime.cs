namespace Delobytes.App.Backend.Contracts.Accounting;

/// <summary>
/// Налоговый режим тенанта. Версия 1 поддерживает только УСН «Доходы».
/// </summary>
public enum TaxRegime
{
    /// <summary>УСН «Доходы».</summary>
    UsnIncome = 1,
}
