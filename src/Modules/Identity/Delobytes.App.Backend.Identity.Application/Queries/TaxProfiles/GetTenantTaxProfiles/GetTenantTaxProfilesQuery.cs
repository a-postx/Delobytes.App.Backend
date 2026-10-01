using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Queries.TaxProfiles.GetTenantTaxProfiles;

/// <summary>
/// Запрос списка версий налогового профиля тенанта.
/// </summary>
public class GetTenantTaxProfilesQuery : IRequest<GetTenantTaxProfilesResponse>
{
    /// <summary>
    /// Gets or sets идентификатор тенанта.
    /// </summary>
    public Guid TenantId { get; set; }
}
