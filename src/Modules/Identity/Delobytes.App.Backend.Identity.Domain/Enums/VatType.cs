namespace Delobytes.App.Backend.Identity.Domain.Enums;

/// <summary>
/// НДС regime applied to the tenant's revenue.
/// </summary>
public enum VatType
{
    /// <summary>Не облагается</summary>
    None = 1,

    /// <summary>5%</summary>
    Five = 2,

    /// <summary>7%</summary>
    Seven = 3,

    /// <summary>22%</summary>
    TwentyTwo = 4,
}
