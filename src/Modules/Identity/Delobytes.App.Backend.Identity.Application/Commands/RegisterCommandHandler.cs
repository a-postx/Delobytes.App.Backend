using Delobytes.App.Backend.Contracts.Errors;
using Delobytes.App.Backend.Contracts.Identity;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain;
using Delobytes.App.Backend.Identity.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands;

/// <summary>
/// Command to register a new user with email/password.
/// </summary>
public class RegisterCommand : IRequest<RegisterResponse>
{
    /// <summary>
    /// Gets or sets the email address.
    /// </summary>
    public string Email { get; set; } = default!;

    /// <summary>
    /// Gets or sets the password.
    /// </summary>
    public string Password { get; set; } = default!;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string? DisplayName { get; set; }
}

/// <summary>
/// Response from register command.
/// </summary>
public class RegisterResponse
{
    /// <summary>
    /// Gets or sets the JWT access token.
    /// </summary>
    public string AccessToken { get; set; } = default!;

    /// <summary>
    /// Gets or sets the created user identifier.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether registration was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user needs to set up a tenant.
    /// </summary>
    public bool RequiresTenantSetup { get; set; }
}

/// <summary>
/// Handler for RegisterCommand.
/// </summary>
public class RegisterCommandHandler : IRequestHandler<RegisterCommand, RegisterResponse>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IUserSessionIssuer _sessionIssuer;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegisterCommandHandler"/> class.
    /// </summary>
    public RegisterCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IUserSessionIssuer sessionIssuer)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _sessionIssuer = sessionIssuer;
    }

    /// <inheritdoc/>
    public async Task<RegisterResponse> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        User? existing = await _userRepository.FindByEmailAsync(request.Email, "Local", cancellationToken);

        if (existing != null)
        {
            throw new AppException(IdentityErrorCodes.EmailAlreadyExists, $"Адрес {request.Email} уже существует ¯\\_(ツ)_/¯");
        }

        User user = new User
        {
            Id = EntityId.New(),
            ExternalId = request.Email,
            IdentityProvider = "Local",
            Email = request.Email,
            NormalizedEmail = EmailNormalizer.Normalize(request.Email),
            DisplayName = request.DisplayName ?? request.Email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };

        _userRepository.Add(user);

        LoginResponse session = await _sessionIssuer.IssueAsync(user, cancellationToken);

        return new RegisterResponse
        {
            UserId = session.UserId,
            Success = true,
            AccessToken = session.AccessToken,
            RequiresTenantSetup = session.RequiresTenantSetup,
        };
    }
}
