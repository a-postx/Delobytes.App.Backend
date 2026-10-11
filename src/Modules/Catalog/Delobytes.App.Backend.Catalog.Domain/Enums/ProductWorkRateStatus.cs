namespace Delobytes.App.Backend.Catalog.Domain.Enums;

/// <summary>
/// Lifecycle of one rate version. Replaces the former boolean, which could not distinguish a
/// version superseded by a newer one from a version removed by the user.
/// </summary>
public enum ProductWorkRateStatus
{
    /// <summary>Currently in force for a future start date, or the newest version of the product.</summary>
    Active,

    /// <summary>Replaced by a newer version appended through POST. Historical only.</summary>
    Superseded,

    /// <summary>Withdrawn by the user, from the version's <c>RemovedFrom</c> date onwards.</summary>
    Removed,
}
