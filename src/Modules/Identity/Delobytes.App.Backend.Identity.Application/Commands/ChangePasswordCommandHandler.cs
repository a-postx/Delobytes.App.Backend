using Delobytes.App.Backend.Contracts.Errors;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands;

/// <summary>
/// Changes the password of a local user and revokes tokens issued before the change.
/// </summary>
public class ChangePasswordCommand : IRequest<Unit>
{
    /// <summary>
    /// Gets or sets the authenticated user identifier.
    /// </summary>
    public Guid UserId { get; set; }

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
/// Handler for ChangePasswordCommand.
/// </summary>
public class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, Unit>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenValidityCache _tokenValidityCache;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChangePasswordCommandHandler"/> class.
    /// </summary>
    public ChangePasswordCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        ITokenValidityCache tokenValidityCache,
        TimeProvider timeProvider)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _tokenValidityCache = tokenValidityCache;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public async Task<Unit> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        User? user = await _userRepository.FindByIdAsync(request.UserId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Пользователь не найден.");

        if (!string.Equals(user.IdentityProvider, "Local", StringComparison.Ordinal)
            || string.IsNullOrEmpty(user.PasswordHash))
        {
            throw new AppException(ErrorCodes.Common.ValidationFailed, "Для этого аккаунта пароль не задан.");
        }

        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            // 422 rather than 401: the caller is authenticated, only the submitted password is wrong.
            throw new AppException(ErrorCodes.Common.ValidationFailed, "Текущий пароль неверен.");
        }

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        user.TokensValidAfter = _timeProvider.GetUtcNow();

        await _userRepository.SaveChangesAsync(cancellationToken);

        _tokenValidityCache.Invalidate(user.Id);

        return Unit.Value;
    }
}
