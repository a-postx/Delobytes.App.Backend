using Delobytes.App.Backend.Contracts.Errors;
using Delobytes.App.Backend.Identity.Application.Commands;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Application.Models;
using Delobytes.App.Backend.Identity.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity;

/// <summary>
/// Tests for the single OAuth callback handler shared by all providers.
/// </summary>
public class OAuthCallbackCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IUserSessionIssuer> _sessionIssuer = new();

    private static Mock<IOAuthProvider> BuildProvider(
        OAuthProviderKind kind,
        string identityProviderName,
        OAuthUserInfo userInfo)
    {
        Mock<IOAuthProvider> provider = new();
        provider.SetupGet(p => p.Kind).Returns(kind);
        provider.SetupGet(p => p.IdentityProviderName).Returns(identityProviderName);
        provider
            .Setup(p => p.AuthorizeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(userInfo);
        return provider;
    }

    private OAuthCallbackCommandHandler BuildHandler(params IOAuthProvider[] providers)
        => new OAuthCallbackCommandHandler(providers, _userRepository.Object, _sessionIssuer.Object);

    private static OAuthCallbackCommand BuildCommand(
        OAuthProviderKind provider = OAuthProviderKind.Google,
        string code = "auth-code",
        string redirectUri = "https://app.example.com/auth/callback")
        => new OAuthCallbackCommand { Provider = provider, Code = code, RedirectUri = redirectUri };

    private static OAuthUserInfo BuildUserInfo(string externalId = "google-sub-001", string email = "ivan@gmail.com")
        => new OAuthUserInfo { ExternalId = externalId, Email = email };

    private static LoginResponse BuildSession(Guid userId)
        => new LoginResponse { UserId = userId, AccessToken = "jwt", RequiresTenantSetup = false };

    [Fact]
    public async Task Handle_UnknownProvider_ThrowsValidationFailed()
    {
        // Arrange
        OAuthCallbackCommandHandler handler = BuildHandler();

        // Act
        AppException exception = await Assert.ThrowsAsync<AppException>(
            () => handler.Handle(BuildCommand(), CancellationToken.None));

        // Assert
        exception.Code.Should().Be(ErrorCodes.Common.ValidationFailed);
    }

    [Fact]
    public async Task Handle_DispatchesOnlyToTheProviderNamedInTheCommand()
    {
        // Arrange
        Mock<IOAuthProvider> google = BuildProvider(OAuthProviderKind.Google, "GoogleID", BuildUserInfo());
        Mock<IOAuthProvider> yandex = BuildProvider(OAuthProviderKind.Yandex, "YandexID", BuildUserInfo(externalId: "yandex-id"));

        User user = new User
        {
            Id = Guid.NewGuid(),
            ExternalId = "google-sub-001",
            IdentityProvider = "GoogleID",
            Email = "ivan@gmail.com",
            NormalizedEmail = "ivan@gmail.com",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _userRepository
            .Setup(r => r.FindByExternalIdAsync("google-sub-001", "GoogleID", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _sessionIssuer
            .Setup(s => s.IssueAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSession(user.Id));

        // Act
        await BuildHandler(google.Object, yandex.Object).Handle(
            BuildCommand(code: "the-code", redirectUri: "https://app.example.com/auth/google/callback"),
            CancellationToken.None);

        // Assert
        google.Verify(
            p => p.AuthorizeAsync("the-code", "https://app.example.com/auth/google/callback", It.IsAny<CancellationToken>()),
            Times.Once);
        yandex.Verify(
            p => p.AuthorizeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_NewUser_CreatesUserWithProviderIdentityAndEmail()
    {
        // Arrange
        Mock<IOAuthProvider> google = BuildProvider(
            OAuthProviderKind.Google,
            "GoogleID",
            BuildUserInfo(externalId: "new-sub", email: "new@googlemail.com"));

        _userRepository
            .Setup(r => r.FindByExternalIdAsync("new-sub", "GoogleID", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        User? addedUser = null;
        _userRepository
            .Setup(r => r.Add(It.IsAny<User>()))
            .Callback<User>(u => addedUser = u);

        OAuthCallbackCommandHandler handler = BuildHandler(google.Object);
        _sessionIssuer
            .Setup(s => s.IssueAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User u, CancellationToken _) => BuildSession(u.Id));

        // Act
        await handler.Handle(BuildCommand(), CancellationToken.None);

        // Assert
        addedUser.Should().NotBeNull();
        addedUser!.ExternalId.Should().Be("new-sub");
        addedUser.IdentityProvider.Should().Be("GoogleID");
        addedUser.Email.Should().Be("new@googlemail.com");
        addedUser.NormalizedEmail.Should().Be("new@googlemail.com");
        addedUser.IsActive.Should().BeTrue();
        addedUser.Id.Should().NotBeEmpty();

        _sessionIssuer.Verify(s => s.IssueAsync(addedUser, It.IsAny<CancellationToken>()), Times.Once);
        _userRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NewExternalIdWithSameProviderEmail_ThrowsConflict()
    {
        // Arrange
        Mock<IOAuthProvider> google = BuildProvider(OAuthProviderKind.Google, "GoogleID", BuildUserInfo());

        _userRepository
            .Setup(r => r.FindByExternalIdAsync("google-sub-001", "GoogleID", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        _userRepository
            .Setup(r => r.FindByEmailAsync("ivan@gmail.com", "GoogleID", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = Guid.NewGuid(),
                ExternalId = "another-sub",
                IdentityProvider = "GoogleID",
                Email = "ivan@gmail.com",
                NormalizedEmail = "ivan@gmail.com",
                IsActive = true,
            });

        // Act
        AppException exception = await Assert.ThrowsAsync<AppException>(
            () => BuildHandler(google.Object).Handle(BuildCommand(), CancellationToken.None));

        // Assert
        exception.Code.Should().Be(ErrorCodes.Common.Conflict);
        _userRepository.Verify(r => r.Add(It.IsAny<User>()), Times.Never);
        _sessionIssuer.Verify(s => s.IssueAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NewUserWithoutEmail_SkipsEmailConflictCheck()
    {
        // Arrange
        Mock<IOAuthProvider> yandex = BuildProvider(
            OAuthProviderKind.Yandex,
            "YandexID",
            BuildUserInfo(externalId: "yandex-id", email: string.Empty));

        _userRepository
            .Setup(r => r.FindByExternalIdAsync("yandex-id", "YandexID", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        OAuthCallbackCommandHandler handler = BuildHandler(yandex.Object);
        _sessionIssuer
            .Setup(s => s.IssueAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User u, CancellationToken _) => BuildSession(u.Id));

        // Act
        await handler.Handle(BuildCommand(OAuthProviderKind.Yandex), CancellationToken.None);

        // Assert
        _userRepository.Verify(
            r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _userRepository.Verify(r => r.Add(It.IsAny<User>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ExistingInactiveUser_ThrowsForbiddenAndIssuesNoSession()
    {
        // Arrange
        Mock<IOAuthProvider> google = BuildProvider(OAuthProviderKind.Google, "GoogleID", BuildUserInfo());

        _userRepository
            .Setup(r => r.FindByExternalIdAsync("google-sub-001", "GoogleID", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = Guid.NewGuid(),
                ExternalId = "google-sub-001",
                IdentityProvider = "GoogleID",
                Email = "ivan@gmail.com",
                NormalizedEmail = "ivan@gmail.com",
                IsActive = false,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        // Act
        AppException exception = await Assert.ThrowsAsync<AppException>(
            () => BuildHandler(google.Object).Handle(BuildCommand(), CancellationToken.None));

        // Assert
        exception.Code.Should().Be(ErrorCodes.Common.Forbidden);
        _sessionIssuer.Verify(s => s.IssueAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ExistingUser_DoesNotCreateUserAndReturnsSession()
    {
        // Arrange
        Mock<IOAuthProvider> google = BuildProvider(OAuthProviderKind.Google, "GoogleID", BuildUserInfo());

        User user = new User
        {
            Id = Guid.NewGuid(),
            ExternalId = "google-sub-001",
            IdentityProvider = "GoogleID",
            Email = "ivan@gmail.com",
            NormalizedEmail = "ivan@gmail.com",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _userRepository
            .Setup(r => r.FindByExternalIdAsync("google-sub-001", "GoogleID", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _sessionIssuer
            .Setup(s => s.IssueAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSession(user.Id));

        // Act
        LoginResponse response = await BuildHandler(google.Object).Handle(BuildCommand(), CancellationToken.None);

        // Assert
        response.UserId.Should().Be(user.Id);
        response.AccessToken.Should().Be("jwt");
        _userRepository.Verify(r => r.Add(It.IsAny<User>()), Times.Never);
    }
}
