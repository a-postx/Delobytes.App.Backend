namespace Delobytes.App.Backend.Identity.Application.Models;

/// <summary>
/// User profile data returned by the Google OAuth user-info endpoint.
/// Only the fields required for local account resolution are mapped.
/// </summary>
public class GoogleUserInfo
{
    /// <summary>
    /// Gets or sets the unique Google user identifier (subject claim).
    /// </summary>
    public string Sub { get; set; } = default!;

    /// <summary>
    /// Gets or sets the user's email address.
    /// </summary>
    public string Email { get; set; } = default!;

    /// <summary>
    /// Gets or sets a value indicating whether the email address has been verified by Google.
    /// </summary>
    public bool EmailVerified { get; set; }
}
