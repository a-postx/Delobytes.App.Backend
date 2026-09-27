namespace Delobytes.App.Backend.Catalog.Domain.Enums;

/// <summary>
/// Defines the source from which a product was created.
/// </summary>
public enum CreationSource
{
    /// <summary>
    /// Product was created manually by a user.
    /// </summary>
    Manual = 0,

    /// <summary>
    /// Product was imported from Wildberries marketplace.
    /// </summary>
    WildberriesImport = 1
}
