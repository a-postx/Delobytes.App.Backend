namespace Delobytes.App.Backend.Identity.Application.Commands.CreateTenant;

/// <summary>
/// Response from CreateTenant command.
/// </summary>
public class CreateTenantResponse
{
    /// <summary>
    /// Gets or sets the created tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the JWT access token with tenant context.
    /// </summary>
    public string AccessToken { get; set; } = default!;
}
