namespace Delobytes.App.Backend.Identity.Application.Commands.TaxProfiles.DeleteTenantTaxProfile;

/// <summary>
/// Ответ на команду удаления налогового профиля.
/// </summary>
public class DeleteTenantTaxProfileResponse
{
    /// <summary>
    /// Gets or sets признак того, что профиль не найден или принадлежит другому тенанту.
    /// </summary>
    public bool Found { get; set; }

    /// <summary>
    /// Gets or sets признак того, что удаляемая версия не является последней.
    /// </summary>
    public bool NotLatest { get; set; }
}
