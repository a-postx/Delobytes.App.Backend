using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Queries.GetTenantLegalEntity;

/// <summary>
/// Query to retrieve tenant legal entity settings.
/// </summary>
public class GetTenantLegalEntityQuery : IRequest<GetTenantLegalEntityResponse>
{
    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }
}
