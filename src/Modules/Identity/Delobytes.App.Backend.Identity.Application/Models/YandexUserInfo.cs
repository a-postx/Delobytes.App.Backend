namespace Delobytes.App.Backend.Identity.Application.Models;

/// <summary>
/// User profile data returned by the Yandex ID user-info endpoint.
/// </summary>
public class YandexUserInfo
{
    /// <summary>
    /// Gets or sets the unique Yandex user identifier.
    /// </summary>
    public string Id { get; set; } = default!;

    /// <summary>
    /// Gets or sets the Yandex login (username).
    /// </summary>
    public string Login { get; set; } = default!;

    /// <summary>
    /// Gets or sets the user's default email address.
    /// </summary>
    public string DefaultEmail { get; set; } = default!;

    /// <summary>
    /// Gets or sets the display name chosen by the user.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the user's first name.
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// Gets or sets the user's last name.
    /// </summary>
    public string? LastName { get; set; }
}
