using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.CreateTenant;

/// <summary>
/// Command to create a new tenant for a user.
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

    /// <summary>
    /// Gets or sets the current active tenant identifier. 
    /// Required when creating additional tenant (must be Administrator).
    /// Null for first-time tenant creation.
    /// </summary>
    public Guid? CurrentTenantId { get; set; }
}
