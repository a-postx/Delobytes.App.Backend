using Delobytes.App.Backend.Contracts.Errors;
using Delobytes.App.Backend.Identity.Application.Commands;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Identity.Tests.Commands;

/// <summary>
/// Tests for LoginCommandHandler. Tenant selection and token issuance live in
/// <c>UserSessionIssuerTests</c>; here only credential checks and delegation are covered.
/// </summary>
public class LoginCommandHandlerTests
{
    private const string LocalProvider = "Local";

    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IUserSessionIssuer> _sessionIssuer = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly LoginCommandHandler _handler;

    public LoginCommandHandlerTests()
    {
        _handler = new LoginCommandHandler(
            _userRepository.Object,
            _sessionIssuer.Object,
            _passwordHasher.Object);
    }

    private static User BuildUser(bool isActive = true)
        => new User
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            NormalizedEmail = "test@example.com",
            ExternalId = "test@example.com",
            IdentityProvider = LocalProvider,
            PasswordHash = "hashed_password",
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static LoginCommand BuildCommand(string email = "test@example.com", string password = "password123")
        => new LoginCommand { Email = email, Password = password };

    [Fact]
    public async Task Handle_ValidCredentials_DelegatesSessionIssuanceToSharedIssuer()
    {
        // Arrange
        User user = BuildUser();
        LoginResponse expected = new LoginResponse
        {
            AccessToken = "jwt_token",
            UserId = user.Id,
            TenantId = Guid.NewGuid(),
            RequiresTenantSetup = false,
        };

        _userRepository
            .Setup(r => r.FindByEmailAsync("test@example.com", LocalProvider, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasher
            .Setup(h => h.Verify("password123", "hashed_password"))
            .Returns(true);
        _sessionIssuer
            .Setup(s => s.IssueAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        LoginResponse response = await _handler.Handle(BuildCommand(), CancellationToken.None);

        // Assert
        response.Should().BeSameAs(expected);
        _sessionIssuer.Verify(s => s.IssueAsync(user, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UnknownEmail_ThrowsUnauthorizedWithoutIssuingSession()
    {
        // Arrange
        _userRepository
            .Setup(r => r.FindByEmailAsync("missing@example.com", LocalProvider, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        Func<Task> act = async () => await _handler.Handle(BuildCommand(email: "missing@example.com"), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _sessionIssuer.Verify(s => s.IssueAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WrongPassword_ThrowsUnauthorizedWithoutIssuingSession()
    {
        // Arrange
        User user = BuildUser();

        _userRepository
            .Setup(r => r.FindByEmailAsync("test@example.com", LocalProvider, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasher
            .Setup(h => h.Verify("password123", "hashed_password"))
            .Returns(false);

        // Act
        Func<Task> act = async () => await _handler.Handle(BuildCommand(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _sessionIssuer.Verify(s => s.IssueAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UserWithoutPasswordHash_ThrowsUnauthorized()
    {
        // Arrange
        User user = BuildUser();
        user.PasswordHash = null;

        _userRepository
            .Setup(r => r.FindByEmailAsync("test@example.com", LocalProvider, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        Func<Task> act = async () => await _handler.Handle(BuildCommand(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _passwordHasher.Verify(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Handle_InactiveUser_ThrowsForbiddenWithoutIssuingSession()
    {
        // Arrange
        User user = BuildUser(isActive: false);

        _userRepository
            .Setup(r => r.FindByEmailAsync("test@example.com", LocalProvider, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasher
            .Setup(h => h.Verify("password123", "hashed_password"))
            .Returns(true);

        // Act
        AppException exception = await Assert.ThrowsAsync<AppException>(
            () => _handler.Handle(BuildCommand(), CancellationToken.None));

        // Assert
        exception.Code.Should().Be(ErrorCodes.Common.Forbidden);
        _sessionIssuer.Verify(s => s.IssueAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
