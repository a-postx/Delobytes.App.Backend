using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.CreateTenant;

/// <summary>
/// Command to create a new tenant for a user who doesn't have any tenant memberships.
/// </summary>
public class CreateTenantCommand : IRequest<CreateTenantResponse>
{
    /// <summary>
    /// Gets or sets the user identifier.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the tenant name.
    /// </summary>
    public string TenantName { get; set; } = default!;
}
