using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Contracts.Errors;
using Delobytes.App.Backend.Identity.Application.Commands;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Moq;

namespace Delobytes.App.Backend.Identity.Tests.Commands;

public class LoginCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<ITenantMembershipRepository> _membershipRepositoryMock;
    private readonly Mock<IJwtTokenService> _jwtTokenServiceMock;
    private readonly Mock<IPasswordHasher> _passwordHasherMock;
    private readonly LoginCommandHandler _handler;

    public LoginCommandHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _membershipRepositoryMock = new Mock<ITenantMembershipRepository>();
        _jwtTokenServiceMock = new Mock<IJwtTokenService>();
        _passwordHasherMock = new Mock<IPasswordHasher>();

        _handler = new LoginCommandHandler(
            _userRepositoryMock.Object,
            _membershipRepositoryMock.Object,
            _jwtTokenServiceMock.Object,
            _passwordHasherMock.Object);
    }

    [Fact]
    public async Task Handle_WhenUserHasLastActiveTenantId_ShouldLoginToLastActiveTenant()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenant1Id = Guid.NewGuid();
        Guid tenant2Id = Guid.NewGuid();
        string email = "test@example.com";
        string password = "password123";
        string passwordHash = "hashed_password";

        User user = new User
        {
            Id = userId,
            Email = email,
            PasswordHash = passwordHash,
            ExternalId = email,
            IdentityProvider = "Local",
            IsActive = true,
            LastActiveTenantId = tenant2Id,
            CreatedAt = DateTimeOffset.UtcNow
        };

        List<TenantMembership> memberships = new List<TenantMembership>
        {
            new TenantMembership
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TenantId = tenant1Id,
                Role = Role.Administrator,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new TenantMembership
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TenantId = tenant2Id,
                Role = Role.Administrator,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            }
        };

        LoginCommand command = new LoginCommand
        {
            Email = email,
            Password = password
        };

        _userRepositoryMock
            .Setup(x => x.FindByEmailAsync(email, "Local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(x => x.Verify(password, passwordHash))
            .Returns(true);

        _membershipRepositoryMock
            .Setup(x => x.GetActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(memberships);

        _jwtTokenServiceMock
            .Setup(x => x.GenerateToken(userId, tenant2Id, Role.Administrator))
            .Returns("jwt_token");

        _userRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        LoginResponse response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(tenant2Id, response.TenantId);
        Assert.Equal("jwt_token", response.AccessToken);
        Assert.False(response.RequiresTenantSetup);

        _userRepositoryMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task Handle_WhenLastActiveTenantIdIsNull_ShouldLoginToFirstTenant()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenant1Id = Guid.NewGuid();
        Guid tenant2Id = Guid.NewGuid();
        string email = "test@example.com";
        string password = "password123";
        string passwordHash = "hashed_password";

        User user = new User
        {
            Id = userId,
            Email = email,
            PasswordHash = passwordHash,
            ExternalId = email,
            IdentityProvider = "Local",
            IsActive = true,
            LastActiveTenantId = null,
            CreatedAt = DateTimeOffset.UtcNow
        };

        List<TenantMembership> memberships = new List<TenantMembership>
        {
            new TenantMembership
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TenantId = tenant1Id,
                Role = Role.Administrator,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new TenantMembership
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TenantId = tenant2Id,
                Role = Role.Administrator,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            }
        };

        LoginCommand command = new LoginCommand
        {
            Email = email,
            Password = password
        };

        _userRepositoryMock
            .Setup(x => x.FindByEmailAsync(email, "Local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(x => x.Verify(password, passwordHash))
            .Returns(true);

        _membershipRepositoryMock
            .Setup(x => x.GetActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(memberships);

        _jwtTokenServiceMock
            .Setup(x => x.GenerateToken(userId, tenant1Id, Role.Administrator))
            .Returns("jwt_token");

        _userRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        LoginResponse response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(tenant1Id, response.TenantId);
        Assert.Equal("jwt_token", response.AccessToken);
        Assert.False(response.RequiresTenantSetup);
        Assert.Equal(tenant1Id, user.LastActiveTenantId);
    }

    [Fact]
    public async Task Handle_WhenLastActiveTenantIdNotInMemberships_ShouldLoginToFirstTenant()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenant1Id = Guid.NewGuid();
        Guid tenant2Id = Guid.NewGuid();
        Guid nonExistentTenantId = Guid.NewGuid();
        string email = "test@example.com";
        string password = "password123";
        string passwordHash = "hashed_password";

        User user = new User
        {
            Id = userId,
            Email = email,
            PasswordHash = passwordHash,
            ExternalId = email,
            IdentityProvider = "Local",
            IsActive = true,
            LastActiveTenantId = nonExistentTenantId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        List<TenantMembership> memberships = new List<TenantMembership>
        {
            new TenantMembership
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TenantId = tenant1Id,
                Role = Role.Administrator,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new TenantMembership
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TenantId = tenant2Id,
                Role = Role.Administrator,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            }
        };

        LoginCommand command = new LoginCommand
        {
            Email = email,
            Password = password
        };

        _userRepositoryMock
            .Setup(x => x.FindByEmailAsync(email, "Local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(x => x.Verify(password, passwordHash))
            .Returns(true);

        _membershipRepositoryMock
            .Setup(x => x.GetActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(memberships);

        _jwtTokenServiceMock
            .Setup(x => x.GenerateToken(userId, tenant1Id, Role.Administrator))
            .Returns("jwt_token");

        _userRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        LoginResponse response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(tenant1Id, response.TenantId);
        Assert.Equal("jwt_token", response.AccessToken);
        Assert.False(response.RequiresTenantSetup);
        Assert.Equal(tenant1Id, user.LastActiveTenantId);
    }

    [Fact]
    public async Task Handle_ShouldUpdateLastActiveTenantIdAfterLogin()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        string email = "test@example.com";
        string password = "password123";
        string passwordHash = "hashed_password";

        User user = new User
        {
            Id = userId,
            Email = email,
            PasswordHash = passwordHash,
            ExternalId = email,
            IdentityProvider = "Local",
            IsActive = true,
            LastActiveTenantId = null,
            CreatedAt = DateTimeOffset.UtcNow
        };

        List<TenantMembership> memberships = new List<TenantMembership>
        {
            new TenantMembership
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TenantId = tenantId,
                Role = Role.Administrator,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            }
        };

        LoginCommand command = new LoginCommand
        {
            Email = email,
            Password = password
        };

        _userRepositoryMock
            .Setup(x => x.FindByEmailAsync(email, "Local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(x => x.Verify(password, passwordHash))
            .Returns(true);

        _membershipRepositoryMock
            .Setup(x => x.GetActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(memberships);

        _jwtTokenServiceMock
            .Setup(x => x.GenerateToken(userId, tenantId, Role.Administrator))
            .Returns("jwt_token");

        _userRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        LoginResponse response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal(tenantId, user.LastActiveTenantId);
        Assert.NotNull(user.LastLoginAt);

        _userRepositoryMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task Handle_WhenUserDoesNotExist_ThrowsUnauthorizedAndDoesNotIssueToken()
    {
        // Arrange
        LoginCommand command = new LoginCommand { Email = "missing@example.com", Password = "password123" };

        _userRepositoryMock
            .Setup(x => x.FindByEmailAsync(command.Email, "Local", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _handler.Handle(command, CancellationToken.None));

        _jwtTokenServiceMock.Verify(
            x => x.GenerateToken(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Role?>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPasswordHashIsMissing_ThrowsUnauthorizedWithoutCallingHasher()
    {
        // Arrange
        User user = new User
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            ExternalId = "user@example.com",
            IdentityProvider = "Local",
            PasswordHash = null,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        LoginCommand command = new LoginCommand { Email = user.Email, Password = "password123" };

        _userRepositoryMock
            .Setup(x => x.FindByEmailAsync(command.Email, "Local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _handler.Handle(command, CancellationToken.None));

        _passwordHasherMock.Verify(
            x => x.Verify(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPasswordIsWrong_ThrowsUnauthorized()
    {
        // Arrange
        User user = new User
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            ExternalId = "user@example.com",
            IdentityProvider = "Local",
            PasswordHash = "hashed_password",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        LoginCommand command = new LoginCommand { Email = user.Email, Password = "wrong" };

        _userRepositoryMock
            .Setup(x => x.FindByEmailAsync(command.Email, "Local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(x => x.Verify(command.Password, user.PasswordHash))
            .Returns(false);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenUserIsInactive_ThrowsForbiddenAndDoesNotIssueToken()
    {
        // Arrange
        User user = new User
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            ExternalId = "user@example.com",
            IdentityProvider = "Local",
            PasswordHash = "hashed_password",
            IsActive = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        LoginCommand command = new LoginCommand { Email = user.Email, Password = "password123" };

        _userRepositoryMock
            .Setup(x => x.FindByEmailAsync(command.Email, "Local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(x => x.Verify(command.Password, user.PasswordHash))
            .Returns(true);

        // Act
        AppException exception = await Assert.ThrowsAsync<AppException>(
            () => _handler.Handle(command, CancellationToken.None));

        // Assert
        Assert.Equal(ErrorCodes.Common.Forbidden, exception.Code);

        _jwtTokenServiceMock.Verify(
            x => x.GenerateToken(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Role?>()),
            Times.Never);

        _membershipRepositoryMock.Verify(
            x => x.GetActiveByUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenUserIsInactiveAndPasswordIsWrong_ThrowsUnauthorized()
    {
        // Arrange
        User user = new User
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            ExternalId = "user@example.com",
            IdentityProvider = "Local",
            PasswordHash = "hashed_password",
            IsActive = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        LoginCommand command = new LoginCommand { Email = user.Email, Password = "wrong" };

        _userRepositoryMock
            .Setup(x => x.FindByEmailAsync(command.Email, "Local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(x => x.Verify(command.Password, user.PasswordHash))
            .Returns(false);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _handler.Handle(command, CancellationToken.None));
    }
}
