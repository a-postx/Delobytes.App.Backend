namespace Delobytes.App.Backend.Integrations.Domain.Enums;

public enum JobType
{
    ProductsImport = 2,

    /// <summary>
    /// Scheduled Wildberries orders delta/backfill import.
    /// </summary>
    WbOrdersImport = 3,

    /// <summary>
    /// Scheduled Ozon FBO orders delta/backfill import.
    /// </summary>
    OzonOrdersImportFbo = 4,

    /// <summary>
    /// Scheduled Ozon FBS orders delta/backfill import.
    /// </summary>
    OzonOrdersImportFbs = 5
}
