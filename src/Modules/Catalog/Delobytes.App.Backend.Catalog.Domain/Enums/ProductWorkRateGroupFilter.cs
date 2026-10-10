namespace Delobytes.App.Backend.Catalog.Domain.Enums;

/// <summary>
/// Group-level filter for the product work rate list, where a "group" is one product with all of
/// its rate versions. A product matches <see cref="Active"/> or <see cref="Inactive"/> when at
/// least one of its versions has that status, regardless of how many other versions have the
/// opposite one.
/// </summary>
public enum ProductWorkRateGroupFilter
{
    /// <summary>Products with at least one active version.</summary>
    Active,

    /// <summary>
    /// Products with at least one version of any status. Products that have no rate version at
    /// all are not included: the endpoint reads the ProductWorkRates table, so a product with no
    /// rows there cannot be represented.
    /// </summary>
    All,

    /// <summary>Products with at least one inactive (superseded or removed) version.</summary>
    Inactive,
}
