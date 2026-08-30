using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Queries.GetCurrentUser;

/// <summary>
/// Query to retrieve the current authenticated user with their active tenant.
/// </summary>
public class GetCurrentUserQuery : IRequest<GetCurrentUserResponse>
{
    /// <summary>
    /// Gets or sets the user identifier extracted from JWT claims.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the tenant identifier extracted from JWT claims.
    /// </summary>
    public Guid TenantId { get; set; }
}
