using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Queries.GetTenantMembers;

/// <summary>
/// Query to retrieve all members and pending invitations for a tenant.
/// </summary>
public class GetTenantMembersQuery : IRequest<GetTenantMembersResponse>
{
    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the user identifier who is requesting the data.
    /// </summary>
    public Guid RequestedByUserId { get; set; }
}
