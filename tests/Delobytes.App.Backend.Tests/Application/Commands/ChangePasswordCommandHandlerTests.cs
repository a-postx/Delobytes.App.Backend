using Delobytes.App.Backend.Contracts.Errors;
using Delobytes.App.Backend.Identity.Application.Commands;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using FluentAssertions;
using Moq;

namespace Delobytes.App.Backend.Identity.Tests.Commands;

/// <summary>
/// Tests for ChangePasswordCommandHandler.
/// </summary>
public class ChangePasswordCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<ITokenValidityCache> _tokenValidityCache = new();
    private readonly ChangePasswordCommandHandler _handler;

    public ChangePasswordCommandHandlerTests()
    {
        _handler = new ChangePasswordCommandHandler(
            _userRepository.Object,
            _passwordHasher.Object,
            _tokenValidityCache.Object,
            new FixedTimeProvider(Now));
    }

    private static User BuildUser(string provider = "Local", string? passwordHash = "old-hash", bool isActive = true)
        => new User
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            NormalizedEmail = "user@example.com",
            ExternalId = "user@example.com",
            IdentityProvider = provider,
            PasswordHash = passwordHash,
            IsActive = isActive,
            CreatedAt = Now,
        };

    private static ChangePasswordCommand BuildCommand(Guid userId)
        => new ChangePasswordCommand
        {
            UserId = userId,
            CurrentPassword = "old-password",
            NewPassword = "new-password",
        };

    [Fact]
    public async Task Handle_ValidCurrentPassword_RehashesAndRevokesIssuedTokens()
    {
        // Arrange
        User user = BuildUser();
        ChangePasswordCommand command = BuildCommand(user.Id);

        _userRepository
            .Setup(r => r.FindByIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasher
            .Setup(h => h.Verify(command.CurrentPassword, "old-hash"))
            .Returns(true);

        _passwordHasher
            .Setup(h => h.Hash(command.NewPassword))
            .Returns("new-hash");

        _userRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        user.PasswordHash.Should().Be("new-hash");
        user.TokensValidAfter.Should().Be(Now);

        _userRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _tokenValidityCache.Verify(c => c.Invalidate(user.Id), Times.Once);
    }

    [Fact]
    public async Task Handle_WrongCurrentPassword_ReturnsValidationErrorAndChangesNothing()
    {
        // Arrange
        User user = BuildUser();
        ChangePasswordCommand command = BuildCommand(user.Id);

        _userRepository
            .Setup(r => r.FindByIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasher
            .Setup(h => h.Verify(command.CurrentPassword, "old-hash"))
            .Returns(false);

        // Act
        AppException exception = await Assert.ThrowsAsync<AppException>(
            () => _handler.Handle(command, CancellationToken.None));

        // Assert
        exception.Code.Should().Be(ErrorCodes.Common.ValidationFailed);
        user.PasswordHash.Should().Be("old-hash");
        user.TokensValidAfter.Should().BeNull();

        _passwordHasher.Verify(h => h.Hash(It.IsAny<string>()), Times.Never);
        _userRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _tokenValidityCache.Verify(c => c.Invalidate(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UserNotFound_ThrowsUnauthorized()
    {
        // Arrange
        ChangePasswordCommand command = BuildCommand(Guid.NewGuid());

        _userRepository
            .Setup(r => r.FindByIdAsync(command.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ExternalUserWithoutPassword_ReturnsValidationError()
    {
        // Arrange
        User user = BuildUser(provider: "YandexID", passwordHash: null);
        ChangePasswordCommand command = BuildCommand(user.Id);

        _userRepository
            .Setup(r => r.FindByIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        AppException exception = await Assert.ThrowsAsync<AppException>(
            () => _handler.Handle(command, CancellationToken.None));

        // Assert
        exception.Code.Should().Be(ErrorCodes.Common.ValidationFailed);
        _passwordHasher.Verify(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _userRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
