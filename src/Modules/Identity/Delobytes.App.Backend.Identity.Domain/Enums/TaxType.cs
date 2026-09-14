namespace Delobytes.App.Backend.Identity.Domain.Enums;

/// <summary>
/// Tax regime applied to the tenant's revenue.
/// </summary>
public enum TaxType
{
    /// <summary>УСН</summary>
    Usn = 1,

    /// <summary>ОСНО</summary>
    Osno = 2,

    /// <summary>НПД</summary>
    Npd = 3,
}
