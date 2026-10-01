using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Queries.TaxProfiles.GetActiveTenantTaxProfile;

/// <summary>
/// Запрос действующего на указанный момент налогового профиля тенанта.
/// </summary>
public class GetActiveTenantTaxProfileQuery : IRequest<GetActiveTenantTaxProfileResponse>
{
    /// <summary>
    /// Gets or sets идентификатор тенанта.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets момент времени; по умолчанию — текущий.
    /// </summary>
    public DateTimeOffset? At { get; set; }
}
