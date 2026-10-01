namespace Delobytes.App.Backend.Identity.Application.Commands.TaxProfiles.CreateTenantTaxProfile;

/// <summary>
/// Ответ на команду создания налогового профиля.
/// </summary>
public class CreateTenantTaxProfileResponse
{
    /// <summary>
    /// Gets or sets идентификатор созданной версии профиля.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets признак конфликта: дата начала не позже последней существующей версии.
    /// </summary>
    public bool Conflict { get; set; }
}
