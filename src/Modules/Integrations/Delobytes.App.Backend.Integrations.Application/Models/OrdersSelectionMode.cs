namespace Delobytes.App.Backend.Integrations.Application.Models;

/// <summary>
/// Channel-neutral selection mode for an orders request. Wildberries maps this to its own
/// <c>flag</c> query parameter; other channels may ignore it.
/// </summary>
public enum OrdersSelectionMode
{
    /// <summary>
    /// Selects orders by their last change date (delta sync). Wildberries <c>flag=0</c>.
    /// </summary>
    LastChanged = 0,

    /// <summary>
    /// Selects orders by their placement date (backfill/reconciliation). Wildberries <c>flag=1</c>.
    /// </summary>
    Placed = 1,
}
