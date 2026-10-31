namespace Delobytes.App.Backend.Integrations.Domain.Enums;

public enum JobType
{
    OrdersSync = 0,
    ProductsImport = 2,

    /// <summary>
    /// Scheduled orders delta/backfill import (Stage 8+), distinct from <see cref="OrdersSync"/>,
    /// which is reserved for the separate full-window reconciliation task type.
    /// </summary>
    OrdersImport = 3,

    /// <summary>
    /// Scheduled Ozon FBO orders delta/backfill import. Keeps its own
    /// <see cref="Delobytes.App.Backend.Integrations.Domain.Entities.OrdersBackfillState"/> and
    /// <see cref="Delobytes.App.Backend.Integrations.Domain.Entities.SyncCheckpoint"/> cursors,
    /// independent from any other orders job type on the same connection.
    /// </summary>
    OrdersImportFbo = 4,

    /// <summary>
    /// Reserved for Ozon FBS orders import. Not scheduled or dispatched yet; disabled until its
    /// own delivery.
    /// </summary>
    OrdersImportFbs = 5
}
