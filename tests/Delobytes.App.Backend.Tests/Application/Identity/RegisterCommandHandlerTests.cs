using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Identity.Application.Commands;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity;

public class RegisterCommandHandlerTests
{
    private const string LocalProvider = "Local";

    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<IUserSessionIssuer> _sessionIssuer = new();
    private readonly RegisterCommandHandler _handler;

    public RegisterCommandHandlerTests()
    {
        _handler = new RegisterCommandHandler(
            _userRepository.Object,
            _passwordHasher.Object,
            _sessionIssuer.Object);
    }

    [Fact]
    public async Task Handle_WhenEmailAlreadyRegistered_ThrowsConflictWithoutCreatingUser()
    {
        // Arrange
        string email = "taken@example.com";
        User existingUser = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            ExternalId = email,
            IdentityProvider = LocalProvider,
            IsActive = true,
        };

        _userRepository
            .Setup(r => r.FindByEmailAsync(email, LocalProvider, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingUser);

        RegisterCommand command = new RegisterCommand
        {
            Email = email,
            Password = "Secret123!",
        };

        // Act
        Func<Task> act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        AppException exception = (await act.Should().ThrowAsync<AppException>()).Which;
        exception.Code.Should().Be(IdentityErrorCodes.EmailAlreadyExists);

        _userRepository.Verify(r => r.Add(It.IsAny<User>()), Times.Never);
        _sessionIssuer.Verify(s => s.IssueAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NewUser_CreatesLocalUserAndMapsSessionFromIssuer()
    {
        // Arrange
        string email = "new@example.com";
        string password = "Secret123!";
        string passwordHash = "bcrypt-hash";
        DateTimeOffset before = DateTimeOffset.UtcNow;

        User? addedUser = null;
        _userRepository
            .Setup(r => r.FindByEmailAsync(email, LocalProvider, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        _userRepository
            .Setup(r => r.Add(It.IsAny<User>()))
            .Callback<User>(user => addedUser = user);
        _passwordHasher
            .Setup(h => h.Hash(password))
            .Returns(passwordHash);

        _sessionIssuer
            .Setup(s => s.IssueAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User user, CancellationToken _) => new LoginResponse
            {
                UserId = user.Id,
                AccessToken = "setup-token",
                RequiresTenantSetup = true,
            });

        RegisterCommand command = new RegisterCommand
        {
            Email = email,
            Password = password,
            DisplayName = "Иван",
        };

        // Act
        RegisterResponse response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        addedUser.Should().NotBeNull();
        addedUser!.Id.Should().NotBe(Guid.Empty);
        addedUser.ExternalId.Should().Be(email);
        addedUser.IdentityProvider.Should().Be(LocalProvider);
        addedUser.Email.Should().Be(email);
        addedUser.NormalizedEmail.Should().Be(email);
        addedUser.DisplayName.Should().Be("Иван");
        addedUser.PasswordHash.Should().Be(passwordHash);
        addedUser.IsActive.Should().BeTrue();
        addedUser.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTimeOffset.UtcNow);
        addedUser.LastActiveTenantId.Should().BeNull();

        response.UserId.Should().Be(addedUser.Id);
        response.AccessToken.Should().Be("setup-token");
        response.Success.Should().BeTrue();
        response.RequiresTenantSetup.Should().BeTrue();

        _passwordHasher.Verify(h => h.Hash(password), Times.Once);
        _userRepository.Verify(r => r.Add(It.IsAny<User>()), Times.Once);
        _sessionIssuer.Verify(s => s.IssueAsync(addedUser, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenDisplayNameNotProvided_FallsBackToEmail()
    {
        // Arrange
        string email = "no-display-name@example.com";

        User? addedUser = null;
        _userRepository
            .Setup(r => r.FindByEmailAsync(email, LocalProvider, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        _userRepository
            .Setup(r => r.Add(It.IsAny<User>()))
            .Callback<User>(user => addedUser = user);
        _passwordHasher
            .Setup(h => h.Hash(It.IsAny<string>()))
            .Returns("bcrypt-hash");
        _sessionIssuer
            .Setup(s => s.IssueAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResponse { AccessToken = "setup-token", RequiresTenantSetup = true });

        RegisterCommand command = new RegisterCommand
        {
            Email = email,
            Password = "Secret123!",
        };

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        addedUser.Should().NotBeNull();
        addedUser!.DisplayName.Should().Be(email);
    }

    [Fact]
    public async Task Handle_WhenIssuerReturnsTenantSession_ResponseDoesNotRequireTenantSetup()
    {
        // Arrange
        _userRepository
            .Setup(r => r.FindByEmailAsync(It.IsAny<string>(), LocalProvider, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        _passwordHasher
            .Setup(h => h.Hash(It.IsAny<string>()))
            .Returns("bcrypt-hash");
        _sessionIssuer
            .Setup(s => s.IssueAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResponse
            {
                UserId = Guid.NewGuid(),
                TenantId = Guid.NewGuid(),
                AccessToken = "full-token",
                RequiresTenantSetup = false,
            });

        RegisterCommand command = new RegisterCommand
        {
            Email = "member@example.com",
            Password = "Secret123!",
        };

        // Act
        RegisterResponse response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        response.AccessToken.Should().Be("full-token");
        response.Success.Should().BeTrue();
        response.RequiresTenantSetup.Should().BeFalse();
    }
}
