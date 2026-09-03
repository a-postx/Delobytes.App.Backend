namespace Delobytes.App.Backend.Identity.Application.Commands.SwitchTenant;

/// <summary>
/// Response for SwitchTenantCommand.
/// </summary>
public class SwitchTenantResponse
{
    /// <summary>
    /// Gets or sets the new JWT access token with updated tenant context.
    /// </summary>
    public string AccessToken { get; set; } = default!;
}
