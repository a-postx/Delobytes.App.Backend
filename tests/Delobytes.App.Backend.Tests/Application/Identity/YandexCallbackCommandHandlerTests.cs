using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Identity.Application.Commands.Login;
using Delobytes.App.Backend.Identity.Application.Commands.YandexCallback;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Application.Models;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Identity;

/// <summary>
/// Unit tests for YandexCallbackCommandHandler.
/// </summary>
public class YandexCallbackCommandHandlerTests
{
    private readonly Mock<IYandexOAuthService> _yandexService;
    private readonly Mock<IUserRepository> _userRepo;
    private readonly Mock<ITenantMembershipRepository> _membershipRepo;
    private readonly Mock<IJwtTokenService> _jwtService;

    public YandexCallbackCommandHandlerTests()
    {
        _yandexService = new Mock<IYandexOAuthService>();
        _userRepo = new Mock<IUserRepository>();
        _membershipRepo = new Mock<ITenantMembershipRepository>();
        _jwtService = new Mock<IJwtTokenService>();
    }

    private YandexCallbackCommandHandler BuildHandler()
        => new YandexCallbackCommandHandler(
            _yandexService.Object,
            _userRepo.Object,
            _membershipRepo.Object,
            _jwtService.Object);

    private static YandexCallbackCommand BuildCommand(string code = "yandex-auth-code", string redirectUri = "https://app.example.com/auth/yandex/callback")
        => new YandexCallbackCommand { Code = code, RedirectUri = redirectUri };

    private static YandexUserInfo BuildUserInfo(string id = "123456789", string login = "ivan", string email = "ivan@yandex.ru")
        => new YandexUserInfo { Id = id, Login = login, DefaultEmail = email };

    private static TenantMembership BuildMembership(Guid userId, Guid tenantId, Role role = Role.Administrator)
        => new TenantMembership { UserId = userId, TenantId = tenantId, Role = role, IsActive = true };

    // ── Token exchange ────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_Always_ExchangesCodeForTokenWithCorrectArguments()
    {
        // Arrange
        YandexCallbackCommand command = BuildCommand(code: "my-code", redirectUri: "https://myapp.ru/callback");

        _yandexService
            .Setup(s => s.ExchangeCodeForTokenAsync("my-code", "https://myapp.ru/callback", It.IsAny<CancellationToken>()))
            .ReturnsAsync("yandex-access-token");

        _yandexService
            .Setup(s => s.GetUserInfoAsync("yandex-access-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildUserInfo());

        Guid tenantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        User existingUser = new User { Id = userId, ExternalId = "123456789", IdentityProvider = "YandexID", Email = "ivan@yandex.ru", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };

        _userRepo
            .Setup(r => r.FindByExternalIdAsync("123456789", "YandexID", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingUser);

        _membershipRepo
            .Setup(r => r.GetActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantMembership> { BuildMembership(userId, tenantId) });

        _jwtService.Setup(s => s.GenerateToken(userId, tenantId, Role.Administrator)).Returns("jwt");

        // Act
        await BuildHandler().Handle(command, CancellationToken.None);

        // Assert
        _yandexService.Verify(
            s => s.ExchangeCodeForTokenAsync("my-code", "https://myapp.ru/callback", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Always_FetchesUserInfoWithReceivedToken()
    {
        // Arrange
        _yandexService
            .Setup(s => s.ExchangeCodeForTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("the-yandex-token");

        _yandexService
            .Setup(s => s.GetUserInfoAsync("the-yandex-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildUserInfo());

        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        User user = new User { Id = userId, ExternalId = "123456789", IdentityProvider = "YandexID", Email = "ivan@yandex.ru", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };

        _userRepo.Setup(r => r.FindByExternalIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _membershipRepo.Setup(r => r.GetActiveByUserAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<TenantMembership> { BuildMembership(userId, tenantId) });
        _jwtService.Setup(s => s.GenerateToken(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Role>())).Returns("jwt");

        // Act
        await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

        // Assert
        _yandexService.Verify(s => s.GetUserInfoAsync("the-yandex-token", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── New user creation ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NewUser_CreatesUserWithCorrectFields()
    {
        // Arrange
        YandexUserInfo userInfo = BuildUserInfo(id: "new-yandex-id", email: "new@yandex.ru");

        _yandexService.Setup(s => s.ExchangeCodeForTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("token");
        _yandexService.Setup(s => s.GetUserInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(userInfo);

        _userRepo
            .Setup(r => r.FindByExternalIdAsync("new-yandex-id", "YandexID", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        User? capturedUser = null;
        _userRepo.Setup(r => r.Add(It.IsAny<User>())).Callback<User>(u => capturedUser = u);
        _userRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _membershipRepo
            .Setup(r => r.GetActiveByUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantMembership>());

        // Act
        LoginResponse response = await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

        // Assert
        capturedUser.Should().NotBeNull();
        capturedUser!.ExternalId.Should().Be("new-yandex-id");
        capturedUser.IdentityProvider.Should().Be("YandexID");
        capturedUser.Email.Should().Be("new@yandex.ru");
        capturedUser.IsActive.Should().BeTrue();
        capturedUser.Id.Should().NotBeEmpty();

        _userRepo.Verify(r => r.Add(It.IsAny<User>()), Times.Once);
        _userRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NewUser_RequiresTenantSetup_ReturnsEmptyToken()
    {
        // Arrange
        _yandexService.Setup(s => s.ExchangeCodeForTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("token");
        _yandexService.Setup(s => s.GetUserInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(BuildUserInfo());

        _userRepo.Setup(r => r.FindByExternalIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);
        _userRepo.Setup(r => r.Add(It.IsAny<User>()));
        _userRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _membershipRepo
            .Setup(r => r.GetActiveByUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantMembership>());

        // Act
        LoginResponse response = await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

        // Assert
        response.RequiresTenantSetup.Should().BeTrue();
        response.AccessToken.Should().BeEmpty();
        response.UserId.Should().NotBeEmpty();

        _jwtService.Verify(s => s.GenerateToken(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Role>()), Times.Never);
    }

    // ── Existing user ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ExistingUser_DoesNotAddNewUser()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        User existingUser = new User { Id = userId, ExternalId = "123456789", IdentityProvider = "YandexID", Email = "ivan@yandex.ru", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };

        _yandexService.Setup(s => s.ExchangeCodeForTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("token");
        _yandexService.Setup(s => s.GetUserInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(BuildUserInfo());

        _userRepo.Setup(r => r.FindByExternalIdAsync("123456789", "YandexID", It.IsAny<CancellationToken>())).ReturnsAsync(existingUser);
        _userRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _membershipRepo.Setup(r => r.GetActiveByUserAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<TenantMembership> { BuildMembership(userId, tenantId) });
        _jwtService.Setup(s => s.GenerateToken(userId, tenantId, Role.Administrator)).Returns("jwt");

        // Act
        await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

        // Assert
        _userRepo.Verify(r => r.Add(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ExistingUser_UpdatesLastLoginAt()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        User existingUser = new User { Id = userId, ExternalId = "123456789", IdentityProvider = "YandexID", Email = "ivan@yandex.ru", IsActive = true, CreatedAt = DateTimeOffset.UtcNow, LastLoginAt = null };

        _yandexService.Setup(s => s.ExchangeCodeForTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("token");
        _yandexService.Setup(s => s.GetUserInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(BuildUserInfo());

        _userRepo.Setup(r => r.FindByExternalIdAsync("123456789", "YandexID", It.IsAny<CancellationToken>())).ReturnsAsync(existingUser);
        _userRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _membershipRepo.Setup(r => r.GetActiveByUserAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<TenantMembership> { BuildMembership(userId, tenantId) });
        _jwtService.Setup(s => s.GenerateToken(userId, tenantId, Role.Administrator)).Returns("jwt");

        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Act
        await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

        // Assert
        existingUser.LastLoginAt.Should().NotBeNull();
        existingUser.LastLoginAt.Should().BeOnOrAfter(before);
    }

    // ── JWT issuance ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ExistingUserWithTenant_ReturnsJwtAndCorrectIds()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        User existingUser = new User { Id = userId, ExternalId = "123456789", IdentityProvider = "YandexID", Email = "ivan@yandex.ru", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };

        _yandexService.Setup(s => s.ExchangeCodeForTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("token");
        _yandexService.Setup(s => s.GetUserInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(BuildUserInfo());

        _userRepo.Setup(r => r.FindByExternalIdAsync("123456789", "YandexID", It.IsAny<CancellationToken>())).ReturnsAsync(existingUser);
        _userRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _membershipRepo.Setup(r => r.GetActiveByUserAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<TenantMembership> { BuildMembership(userId, tenantId) });
        _jwtService.Setup(s => s.GenerateToken(userId, tenantId, Role.Administrator)).Returns("signed-jwt");

        // Act
        LoginResponse response = await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

        // Assert
        response.RequiresTenantSetup.Should().BeFalse();
        response.AccessToken.Should().Be("signed-jwt");
        response.UserId.Should().Be(userId);
        response.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task Handle_ExistingUserWithTenant_CallsGenerateTokenOnce()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        User existingUser = new User { Id = userId, ExternalId = "123456789", IdentityProvider = "YandexID", Email = "ivan@yandex.ru", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };

        _yandexService.Setup(s => s.ExchangeCodeForTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("token");
        _yandexService.Setup(s => s.GetUserInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(BuildUserInfo());

        _userRepo.Setup(r => r.FindByExternalIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(existingUser);
        _userRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _membershipRepo.Setup(r => r.GetActiveByUserAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<TenantMembership> { BuildMembership(userId, tenantId) });
        _jwtService.Setup(s => s.GenerateToken(userId, tenantId, Role.Administrator)).Returns("jwt");

        // Act
        await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

        // Assert
        _jwtService.Verify(s => s.GenerateToken(userId, tenantId, Role.Administrator), Times.Once);
    }

    [Fact]
    public async Task Handle_UserWithMembershipRole_PassesCorrectRoleToJwtService()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        User existingUser = new User { Id = userId, ExternalId = "123456789", IdentityProvider = "YandexID", Email = "ivan@yandex.ru", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };

        _yandexService.Setup(s => s.ExchangeCodeForTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("token");
        _yandexService.Setup(s => s.GetUserInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(BuildUserInfo());

        _userRepo.Setup(r => r.FindByExternalIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(existingUser);
        _userRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _membershipRepo.Setup(r => r.GetActiveByUserAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<TenantMembership> { BuildMembership(userId, tenantId, Role.ReadOnly) });
        _jwtService.Setup(s => s.GenerateToken(userId, tenantId, Role.ReadOnly)).Returns("jwt");

        // Act
        await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

        // Assert
        _jwtService.Verify(s => s.GenerateToken(userId, tenantId, Role.ReadOnly), Times.Once);
    }
}
