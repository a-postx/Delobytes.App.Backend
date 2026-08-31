using Delobytes.App.Backend.Identity.Application.Commands.Login;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.YandexCallback;

/// <summary>
/// Command to complete a Yandex ID OAuth 2.0 authorization-code flow.
/// The backend exchanges the code for a Yandex token, fetches user info,
/// and returns a local JWT just like the regular login flow does.
/// </summary>
public class YandexCallbackCommand : IRequest<LoginResponse>
{
    /// <summary>
    /// Gets or sets the authorization code received from Yandex.
    /// </summary>
    public string Code { get; set; } = default!;

    /// <summary>
    /// Gets or sets the redirect URI that was used in the original authorization request.
    /// Must exactly match the value registered in the Yandex OAuth application settings.
    /// </summary>
    public string RedirectUri { get; set; } = default!;
}
