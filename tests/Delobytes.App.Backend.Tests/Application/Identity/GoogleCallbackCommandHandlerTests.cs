using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Identity.Application.Commands.GoogleCallback;
using Delobytes.App.Backend.Identity.Application.Commands.Login;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Application.Models;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Identity;

/// <summary>
/// Unit tests for GoogleCallbackCommandHandler.
/// </summary>
public class GoogleCallbackCommandHandlerTests
{
    private readonly Mock<IGoogleOAuthService> _googleService;
    private readonly Mock<IUserRepository> _userRepo;
    private readonly Mock<ITenantMembershipRepository> _membershipRepo;
    private readonly Mock<IJwtTokenService> _jwtService;

    public GoogleCallbackCommandHandlerTests()
    {
        _googleService = new Mock<IGoogleOAuthService>();
        _userRepo = new Mock<IUserRepository>();
        _membershipRepo = new Mock<ITenantMembershipRepository>();
        _jwtService = new Mock<IJwtTokenService>();
    }

    private GoogleCallbackCommandHandler BuildHandler()
        => new GoogleCallbackCommandHandler(
            _googleService.Object,
            _userRepo.Object,
            _membershipRepo.Object,
            _jwtService.Object);

    private static GoogleCallbackCommand BuildCommand(string code = "google-auth-code", string redirectUri = "https://app.example.com/auth/google/callback")
        => new GoogleCallbackCommand { Code = code, RedirectUri = redirectUri };

    private static GoogleUserInfo BuildUserInfo(string sub = "google-sub-001", string email = "ivan@gmail.com", bool emailVerified = true)
        => new GoogleUserInfo { Sub = sub, Email = email, EmailVerified = emailVerified };

    private static TenantMembership BuildMembership(Guid userId, Guid tenantId, Role role = Role.Administrator)
        => new TenantMembership { UserId = userId, TenantId = tenantId, Role = role, IsActive = true };

    // ── Token exchange ────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_Always_ExchangesCodeForTokenWithCorrectArguments()
    {
        // Arrange
        GoogleCallbackCommand command = BuildCommand(code: "my-google-code", redirectUri: "https://myapp.ru/auth/google/callback");

        _googleService
            .Setup(s => s.ExchangeCodeForTokenAsync("my-google-code", "https://myapp.ru/auth/google/callback", It.IsAny<CancellationToken>()))
            .ReturnsAsync("google-access-token");

        _googleService
            .Setup(s => s.GetUserInfoAsync("google-access-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildUserInfo());

        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        User existingUser = new User { Id = userId, ExternalId = "google-sub-001", IdentityProvider = "GoogleID", Email = "ivan@gmail.com", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };

        _userRepo
            .Setup(r => r.FindByExternalIdAsync("google-sub-001", "GoogleID", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingUser);

        _membershipRepo
            .Setup(r => r.GetActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantMembership> { BuildMembership(userId, tenantId) });

        _jwtService.Setup(s => s.GenerateToken(userId, tenantId, Role.Administrator)).Returns("jwt");

        // Act
        await BuildHandler().Handle(command, CancellationToken.None);

        // Assert
        _googleService.Verify(
            s => s.ExchangeCodeForTokenAsync("my-google-code", "https://myapp.ru/auth/google/callback", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Always_FetchesUserInfoWithReceivedToken()
    {
        // Arrange
        _googleService
            .Setup(s => s.ExchangeCodeForTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("the-google-token");

        _googleService
            .Setup(s => s.GetUserInfoAsync("the-google-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildUserInfo());

        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        User user = new User { Id = userId, ExternalId = "google-sub-001", IdentityProvider = "GoogleID", Email = "ivan@gmail.com", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };

        _userRepo.Setup(r => r.FindByExternalIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _membershipRepo.Setup(r => r.GetActiveByUserAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<TenantMembership> { BuildMembership(userId, tenantId) });
        _jwtService.Setup(s => s.GenerateToken(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Role>())).Returns("jwt");

        // Act
        await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

        // Assert
        _googleService.Verify(s => s.GetUserInfoAsync("the-google-token", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── New user creation ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NewUser_CreatesUserWithCorrectFields()
    {
        // Arrange
        GoogleUserInfo userInfo = BuildUserInfo(sub: "new-google-sub", email: "new@gmail.com");

        _googleService.Setup(s => s.ExchangeCodeForTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("token");
        _googleService.Setup(s => s.GetUserInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(userInfo);

        _userRepo
            .Setup(r => r.FindByExternalIdAsync("new-google-sub", "GoogleID", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        User? capturedUser = null;
        _userRepo.Setup(r => r.Add(It.IsAny<User>())).Callback<User>(u => capturedUser = u);
        _userRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _membershipRepo
            .Setup(r => r.GetActiveByUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantMembership>());

        // Act
        await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

        // Assert
        capturedUser.Should().NotBeNull();
        capturedUser!.ExternalId.Should().Be("new-google-sub");
        capturedUser.IdentityProvider.Should().Be("GoogleID");
        capturedUser.Email.Should().Be("new@gmail.com");
        capturedUser.IsActive.Should().BeTrue();
        capturedUser.Id.Should().NotBeEmpty();

        _userRepo.Verify(r => r.Add(It.IsAny<User>()), Times.Once);
        _userRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NewUser_NoTenant_RequiresTenantSetup()
    {
        // Arrange
        _googleService.Setup(s => s.ExchangeCodeForTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("token");
        _googleService.Setup(s => s.GetUserInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(BuildUserInfo());

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
        User existingUser = new User { Id = userId, ExternalId = "google-sub-001", IdentityProvider = "GoogleID", Email = "ivan@gmail.com", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };

        _googleService.Setup(s => s.ExchangeCodeForTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("token");
        _googleService.Setup(s => s.GetUserInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(BuildUserInfo());

        _userRepo.Setup(r => r.FindByExternalIdAsync("google-sub-001", "GoogleID", It.IsAny<CancellationToken>())).ReturnsAsync(existingUser);
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
        User existingUser = new User { Id = userId, ExternalId = "google-sub-001", IdentityProvider = "GoogleID", Email = "ivan@gmail.com", IsActive = true, CreatedAt = DateTimeOffset.UtcNow, LastLoginAt = null };

        _googleService.Setup(s => s.ExchangeCodeForTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("token");
        _googleService.Setup(s => s.GetUserInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(BuildUserInfo());

        _userRepo.Setup(r => r.FindByExternalIdAsync("google-sub-001", "GoogleID", It.IsAny<CancellationToken>())).ReturnsAsync(existingUser);
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
        User existingUser = new User { Id = userId, ExternalId = "google-sub-001", IdentityProvider = "GoogleID", Email = "ivan@gmail.com", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };

        _googleService.Setup(s => s.ExchangeCodeForTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("token");
        _googleService.Setup(s => s.GetUserInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(BuildUserInfo());

        _userRepo.Setup(r => r.FindByExternalIdAsync("google-sub-001", "GoogleID", It.IsAny<CancellationToken>())).ReturnsAsync(existingUser);
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
    public async Task Handle_UserWithMembershipRole_PassesCorrectRoleToJwtService()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        User existingUser = new User { Id = userId, ExternalId = "google-sub-001", IdentityProvider = "GoogleID", Email = "ivan@gmail.com", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };

        _googleService.Setup(s => s.ExchangeCodeForTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("token");
        _googleService.Setup(s => s.GetUserInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(BuildUserInfo());

        _userRepo.Setup(r => r.FindByExternalIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(existingUser);
        _userRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _membershipRepo.Setup(r => r.GetActiveByUserAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<TenantMembership> { BuildMembership(userId, tenantId, Role.Manager) });
        _jwtService.Setup(s => s.GenerateToken(userId, tenantId, Role.Manager)).Returns("jwt");

        // Act
        await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

        // Assert
        _jwtService.Verify(s => s.GenerateToken(userId, tenantId, Role.Manager), Times.Once);
    }

    // ── Provider isolation ────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_LooksUpUserByGoogleIdProviderString()
    {
        // Arrange
        _googleService.Setup(s => s.ExchangeCodeForTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("token");
        _googleService.Setup(s => s.GetUserInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(BuildUserInfo(sub: "sub-xyz"));

        _userRepo
            .Setup(r => r.FindByExternalIdAsync("sub-xyz", "GoogleID", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        _userRepo.Setup(r => r.Add(It.IsAny<User>()));
        _userRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _membershipRepo.Setup(r => r.GetActiveByUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<TenantMembership>());

        // Act
        await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

        // Assert — must use "GoogleID", never "YandexID"
        _userRepo.Verify(r => r.FindByExternalIdAsync("sub-xyz", "GoogleID", It.IsAny<CancellationToken>()), Times.Once);
        _userRepo.Verify(r => r.FindByExternalIdAsync(It.IsAny<string>(), "YandexID", It.IsAny<CancellationToken>()), Times.Never);
    }
}
