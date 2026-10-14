namespace Delobytes.App.Backend.Integrations.Domain.Enums;

public enum JobType
{
    OrdersSync = 0,
    ProductsImport = 2,

    /// <summary>
    /// Scheduled orders delta/backfill import (Stage 8+), distinct from <see cref="OrdersSync"/>,
    /// which is reserved for the separate full-window reconciliation task type.
    /// </summary>
    OrdersImport = 3
}
