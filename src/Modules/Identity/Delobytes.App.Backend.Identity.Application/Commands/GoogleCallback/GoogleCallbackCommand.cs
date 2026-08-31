using Delobytes.App.Backend.Identity.Application.Commands.Login;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.GoogleCallback;

/// <summary>
/// Command to complete a Google OAuth 2.0 authorization-code flow.
/// The backend exchanges the code for a Google token, fetches user info,
/// and returns a local JWT just like the regular login flow does.
/// </summary>
public class GoogleCallbackCommand : IRequest<LoginResponse>
{
    /// <summary>
    /// Gets or sets the authorization code received from Google.
    /// </summary>
    public string Code { get; set; } = default!;

    /// <summary>
    /// Gets or sets the redirect URI that was used in the original authorization request.
    /// Must exactly match the value registered in the Google Cloud Console.
    /// </summary>
    public string RedirectUri { get; set; } = default!;
}
