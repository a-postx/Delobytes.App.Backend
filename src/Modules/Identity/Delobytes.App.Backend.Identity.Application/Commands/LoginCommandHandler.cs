using Delobytes.App.Backend.Contracts.Errors;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands;

/// <summary>
/// Command to authenticate a local user with email and password.
/// External identities are authenticated only by the OAuth callback handler.
/// </summary>
public class LoginCommand : IRequest<LoginResponse>
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
/// Handler for LoginCommand.
/// </summary>
public class LoginCommandHandler : IRequestHandler<LoginCommand, LoginResponse>
{
    private readonly IUserRepository _userRepository;
    private readonly IUserSessionIssuer _sessionIssuer;
    private readonly IPasswordHasher _passwordHasher;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoginCommandHandler"/> class.
    /// </summary>
    public LoginCommandHandler(
        IUserRepository userRepository,
        IUserSessionIssuer sessionIssuer,
        IPasswordHasher passwordHasher)
    {
        _userRepository = userRepository;
        _sessionIssuer = sessionIssuer;
        _passwordHasher = passwordHasher;
    }

    /// <inheritdoc/>
    public async Task<LoginResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        User? user = await _userRepository.FindByEmailAsync(request.Email, "Local", cancellationToken);

        // Verify the password before reporting the account state, so an unknown email
        // and a disabled account answer differently only to someone holding the password.
        if (user == null
            || string.IsNullOrEmpty(user.PasswordHash)
            || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Неверный адрес или пароль.");
        }

        if (!user.IsActive)
        {
            throw new AppException(ErrorCodes.Common.Forbidden, "Учётная запись отключена. Обратитесь к администратору.");
        }

        return await _sessionIssuer.IssueAsync(user, cancellationToken);
    }
}
