namespace Delobytes.App.Backend.Contracts.Accounting;

/// <summary>
/// Режим НДС, применяемый к доходу тенанта.
/// </summary>
public enum VatType
{
    /// <summary>Не облагается.</summary>
    None = 1,

    /// <summary>5%</summary>
    Five = 2,

    /// <summary>7%</summary>
    Seven = 3,

    /// <summary>22%</summary>
    TwentyTwo = 4,
}
