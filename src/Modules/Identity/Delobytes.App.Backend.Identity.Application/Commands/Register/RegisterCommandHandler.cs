using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.Register;

/// <summary>
/// Handler for RegisterCommand.
/// </summary>
public class RegisterCommandHandler : IRequestHandler<RegisterCommand, RegisterResponse>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegisterCommandHandler"/> class.
    /// </summary>
    public RegisterCommandHandler(IUserRepository userRepository, IPasswordHasher passwordHasher)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
    }

    /// <inheritdoc/>
    public async Task<RegisterResponse> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        User? existing = await _userRepository.FindByEmailAsync(request.Email, "Local", cancellationToken);

        if (existing != null)
        {
            throw new InvalidOperationException($"User with email {request.Email} already exists.");
        }

        User user = new User
        {
            Id = Guid.NewGuid(),
            ExternalId = request.Email,
            IdentityProvider = "Local",
            Email = request.Email,
            DisplayName = request.DisplayName ?? request.Email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };

        _userRepository.Add(user);
        await _userRepository.SaveChangesAsync(cancellationToken);

        return new RegisterResponse
        {
            UserId = user.Id,
            Success = true,
        };
    }
}
