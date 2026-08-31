using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.UpdateTenantName;

/// <summary>
/// Command to update tenant name.
/// </summary>
public class UpdateTenantNameCommand : IRequest<UpdateTenantNameResponse>
{
    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the new tenant name.
    /// </summary>
    public string Name { get; set; } = default!;
}
