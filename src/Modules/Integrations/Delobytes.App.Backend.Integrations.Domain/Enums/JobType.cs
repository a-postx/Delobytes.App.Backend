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
    /// Scheduled Ozon FBS orders delta/backfill import. Scheduled alongside
    /// <see cref="OrdersImportFbo"/> on the same connection; the scheduler serialises the two so a
    /// connection never runs them concurrently, and each keeps its own backfill and sync cursors.
    /// </summary>
    OrdersImportFbs = 5
}
