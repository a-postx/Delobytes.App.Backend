namespace Delobytes.App.Backend.Catalog.Domain.Enums;

/// <summary>
/// Specifies the type of logistics tariff grid.
/// </summary>
public enum TariffType
{
    /// <summary>WB warehouse logistics tariff, differentiated by region.</summary>
    WbLogistics = 0,

    /// <summary>Fulfilment-centre delivery tariff, differentiated by city and box volume.</summary>
    FulfillmentCenter = 1
}
