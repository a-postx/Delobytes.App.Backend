namespace Delobytes.App.Backend.Identity.Application.Commands.UpdateTenantName;

/// <summary>
/// Response for UpdateTenantNameCommand.
/// </summary>
public class UpdateTenantNameResponse
{
    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the updated tenant name.
    /// </summary>
    public string Name { get; set; } = default!;
}
