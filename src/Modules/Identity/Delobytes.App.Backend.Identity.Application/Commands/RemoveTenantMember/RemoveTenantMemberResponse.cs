namespace Delobytes.App.Backend.Identity.Application.Commands.RemoveTenantMember;

/// <summary>
/// Response returned by RemoveTenantMemberCommand.
/// </summary>
public class RemoveTenantMemberResponse
{
    /// <summary>
    /// Gets or sets a value indicating whether the removal was successful.
    /// </summary>
    public bool Success { get; set; }
}
