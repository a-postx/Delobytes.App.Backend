namespace Delobytes.App.Backend.Integrations.Application.Models;

/// <summary>
/// Channel-specific order schema a request may target. Only meaningful to channels that split
/// orders by fulfilment model (e.g. Ozon FBO vs FBS); other channels ignore it.
/// </summary>
public enum OrdersSchema
{
    /// <summary>
    /// Fulfilment by the marketplace operator.
    /// </summary>
    Fbo = 0,

    /// <summary>
    /// Fulfilment by the seller.
    /// </summary>
    Fbs = 1,
}
