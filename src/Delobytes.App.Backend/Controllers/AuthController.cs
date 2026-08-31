using System.Threading.Tasks;
using Delobytes.App.Backend.Identity.Application.Commands.CreateTenant;
using Delobytes.App.Backend.Identity.Application.Commands.GoogleCallback;
using Delobytes.App.Backend.Identity.Application.Commands.Login;
using Delobytes.App.Backend.Identity.Application.Commands.Register;
using Delobytes.App.Backend.Identity.Application.Commands.YandexCallback;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Delobytes.App.Backend.Controllers;

/// <summary>
/// Authentication and authorization endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthController"/> class.
    /// </summary>
    /// <param name="mediator">Mediator instance.</param>
    public AuthController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Register a new user with email/password.
    /// </summary>
    /// <param name="command">Registration details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Registration result.</returns>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<RegisterResponse>> Register(
        [FromBody] RegisterCommand command,
        CancellationToken cancellationToken)
    {
        RegisterResponse response = await _mediator.Send(command, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Login with email/password or external provider.
    /// </summary>
    /// <param name="command">Login details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Login result with JWT token.</returns>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginCommand command,
        CancellationToken cancellationToken)
    {
        LoginResponse response = await _mediator.Send(command, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Create a new tenant for the current user.
    /// </summary>
    /// <param name="command">Tenant creation details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tenant creation result with JWT token.</returns>
    [HttpPost("create-tenant")]
    [AllowAnonymous]
    public async Task<ActionResult<CreateTenantResponse>> CreateTenant(
        [FromBody] CreateTenantCommand command,
        CancellationToken cancellationToken)
    {
        CreateTenantResponse response = await _mediator.Send(command, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Complete the Yandex ID OAuth 2.0 authorization-code flow.
    /// The client sends the code received from Yandex; the backend exchanges it
    /// for a Yandex token, resolves or creates the local user account, and
    /// returns a local JWT identical in shape to the regular login response.
    /// </summary>
    /// <param name="command">Authorization code and redirect URI.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Login result with JWT token.</returns>
    [HttpPost("yandex/callback")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> YandexCallback(
        [FromBody] YandexCallbackCommand command,
        CancellationToken cancellationToken)
    {
        LoginResponse response = await _mediator.Send(command, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Complete the Google OAuth 2.0 authorization-code flow.
    /// The client sends the code received from Google; the backend exchanges it
    /// for a Google token, resolves or creates the local user account, and
    /// returns a local JWT identical in shape to the regular login response.
    /// </summary>
    /// <param name="command">Authorization code and redirect URI.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Login result with JWT token.</returns>
    [HttpPost("google/callback")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> GoogleCallback(
        [FromBody] GoogleCallbackCommand command,
        CancellationToken cancellationToken)
    {
        LoginResponse response = await _mediator.Send(command, cancellationToken);
        return Ok(response);
    }
}
