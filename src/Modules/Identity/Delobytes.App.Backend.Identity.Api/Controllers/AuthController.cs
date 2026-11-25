using System.Security.Claims;
using System.Threading.Tasks;
using Delobytes.App.Backend.Identity.Application.Commands;
using Delobytes.App.Backend.Identity.Application.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Identity.Api.Controllers;

/// <summary>
/// Authentication and authorization endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<AuthController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthController"/> class.
    /// </summary>
    /// <param name="mediator">Mediator instance.</param>
    /// /// <param name="logger">Logger.</param>
    public AuthController(IMediator mediator, ILogger<AuthController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    /// <summary>
    /// Register a new user with email/password.
    /// </summary>
    /// <param name="command">Registration details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Registration result.</returns>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("register")]
    public async Task<ActionResult<RegisterResponse>> Register(
        [FromBody] RegisterCommand command,
        CancellationToken cancellationToken)
    {
        RegisterResponse response = await _mediator.Send(command, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Login with email/password. External identities use the OAuth callback endpoints.
    /// </summary>
    /// <param name="request">Login details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Login result with JWT token.</returns>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        LoginResponse response = await _mediator.Send(
            new LoginCommand
            {
                Email = request.Email,
                Password = request.Password,
            },
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Change the password of the current local user and revoke existing tokens.
    /// </summary>
    /// <param name="request">Current and new password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>No content on success.</returns>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        string? userIdClaim = User.FindFirstValue("sub");

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out Guid userId))
        {
            return Unauthorized();
        }

        await _mediator.Send(
            new ChangePasswordCommand
            {
                UserId = userId,
                CurrentPassword = request.CurrentPassword,
                NewPassword = request.NewPassword,
            },
            cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Create a new tenant for the current user.
    /// </summary>
    /// <param name="request">Tenant creation details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tenant creation result with JWT token.</returns>
    [HttpPost("create-tenant")]
    [Authorize]
    public async Task<ActionResult<CreateTenantResponse>> CreateTenant(
        [FromBody] CreateTenantRequest request,
        CancellationToken cancellationToken)
    {
        string? userIdClaim = User.FindFirstValue("sub");

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out Guid userId))
        {
            return Unauthorized();
        }

        string? tenantIdClaim = User.FindFirstValue("tenantId");
        Guid? currentTenantId = null;

        if (!string.IsNullOrEmpty(tenantIdClaim) && Guid.TryParse(tenantIdClaim, out Guid parsedTenantId))
        {
            currentTenantId = parsedTenantId;
        }

        CreateTenantResponse response = await _mediator.Send(
            new CreateTenantCommand
            {
                UserId = userId,
                TenantName = request.TenantName,
                CurrentTenantId = currentTenantId,
            },
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Complete the Yandex ID OAuth 2.0 authorization-code flow.
    /// The client sends the code received from Yandex; the backend exchanges it
    /// for a Yandex token, resolves or creates the local user account, and returns a local JWT.
    /// </summary>
    /// <param name="request">Authorization code and redirect URI.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Login result with JWT token.</returns>
    [HttpPost("yandex/callback")]
    [AllowAnonymous]
    [EnableRateLimiting("oauth-callback")]
    public async Task<ActionResult<LoginResponse>> YandexCallback(
        [FromBody] OAuthCallbackRequest request,
        CancellationToken cancellationToken)
    {
        LoginResponse response = await SendCallbackAsync(
            OAuthProviderKind.Yandex, request, cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Complete the Google OAuth 2.0 authorization-code flow.
    /// The client sends the code received from Google; the backend exchanges it for a Google token,
    /// resolves or creates the local user account, and returns a local JWT.
    /// </summary>
    /// <param name="request">Authorization code and redirect URI.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Login result with JWT token.</returns>
    [HttpPost("google/callback")]
    [AllowAnonymous]
    [EnableRateLimiting("oauth-callback")]
    public async Task<ActionResult<LoginResponse>> GoogleCallback(
        [FromBody] OAuthCallbackRequest request,
        CancellationToken cancellationToken)
    {
        LoginResponse response = await SendCallbackAsync(
            OAuthProviderKind.Google, request, cancellationToken);

        return Ok(response);
    }

    private async Task<LoginResponse> SendCallbackAsync(
        OAuthProviderKind provider,
        OAuthCallbackRequest request,
        CancellationToken cancellationToken)
    {
        return await _mediator.Send(
            new OAuthCallbackCommand
            {
                Provider = provider,
                Code = request.Code,
                RedirectUri = request.RedirectUri,
            },
            cancellationToken);
    }
}

/// <summary>
/// Request model for OAuth 2.0 callbacks. The provider comes from the route, not the body.
/// </summary>
public class OAuthCallbackRequest
{
    /// <summary>
    /// Gets or sets the authorization code received from the provider.
    /// </summary>
    public string Code { get; set; } = default!;

    /// <summary>
    /// Gets or sets the redirect URI used in the original authorization request.
    /// </summary>
    public string RedirectUri { get; set; } = default!;
}

/// <summary>
/// Request model for local email/password login.
/// </summary>
public class LoginRequest
{
    /// <summary>
    /// Gets or sets the email address.
    /// </summary>
    public string Email { get; set; } = default!;

    /// <summary>
    /// Gets or sets the password.
    /// </summary>
    public string Password { get; set; } = default!;
}

/// <summary>
/// Request model for changing the current user's password.
/// </summary>
public class ChangePasswordRequest
{
    /// <summary>
    /// Gets or sets the current password.
    /// </summary>
    public string CurrentPassword { get; set; } = default!;

    /// <summary>
    /// Gets or sets the new password.
    /// </summary>
    public string NewPassword { get; set; } = default!;
}

/// <summary>
/// Request model for creating a tenant.
/// </summary>
public class CreateTenantRequest
{
    /// <summary>
    /// Gets or sets the tenant name.
    /// </summary>
    public string TenantName { get; set; } = default!;
}
