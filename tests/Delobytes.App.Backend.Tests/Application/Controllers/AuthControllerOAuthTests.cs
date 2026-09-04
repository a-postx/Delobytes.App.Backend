using System;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime.Internal.Util;
using Delobytes.App.Backend.Controllers;
using Delobytes.App.Backend.Identity.Application.Commands.GoogleCallback;
using Delobytes.App.Backend.Identity.Application.Commands.Login;
using Delobytes.App.Backend.Identity.Application.Commands.YandexCallback;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Controllers;

/// <summary>
/// Tests for AuthController OAuth callback endpoints.
/// </summary>
public class AuthControllerOAuthTests
{
    private readonly Mock<IMediator> _mediator;
    private readonly Mock<ILogger<AuthController>> _logger;

    public AuthControllerOAuthTests()
    {
        _mediator = new Mock<IMediator>();
        _logger = new Mock<ILogger<AuthController>>();
    }

    private AuthController BuildController() => new AuthController(_mediator.Object, _logger.Object);

    // ── Yandex callback ───────────────────────────────────────────────────────

    [Fact]
    public async Task YandexCallback_ValidCommand_ReturnsOkWithLoginResponse()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();

        LoginResponse handlerResponse = new LoginResponse
        {
            AccessToken = "jwt-token",
            UserId = userId,
            TenantId = tenantId,
            RequiresTenantSetup = false,
        };

        _mediator
            .Setup(m => m.Send(It.IsAny<YandexCallbackCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(handlerResponse);

        YandexCallbackCommand command = new YandexCallbackCommand
        {
            Code = "yandex-code-abc",
            RedirectUri = "https://app.example.com/auth/yandex/callback",
        };

        // Act
        ActionResult<LoginResponse> result = await BuildController().YandexCallback(command, CancellationToken.None);

        // Assert
        OkObjectResult okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        LoginResponse response = okResult.Value.Should().BeOfType<LoginResponse>().Subject;
        response.AccessToken.Should().Be("jwt-token");
        response.UserId.Should().Be(userId);
        response.TenantId.Should().Be(tenantId);
        response.RequiresTenantSetup.Should().BeFalse();
    }

    [Fact]
    public async Task YandexCallback_SendsCommandWithCorrectCodeAndRedirectUri()
    {
        // Arrange
        _mediator
            .Setup(m => m.Send(It.IsAny<YandexCallbackCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResponse { UserId = Guid.NewGuid(), RequiresTenantSetup = true, AccessToken = string.Empty });

        YandexCallbackCommand command = new YandexCallbackCommand
        {
            Code = "exact-code-value",
            RedirectUri = "https://myapp.ru/auth/yandex/callback",
        };

        // Act
        await BuildController().YandexCallback(command, CancellationToken.None);

        // Assert
        _mediator.Verify(m => m.Send(
            It.Is<YandexCallbackCommand>(c =>
                c.Code == "exact-code-value" &&
                c.RedirectUri == "https://myapp.ru/auth/yandex/callback"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task YandexCallback_RequiresTenantSetup_ReturnsOkWithFlag()
    {
        // Arrange
        Guid userId = Guid.NewGuid();

        _mediator
            .Setup(m => m.Send(It.IsAny<YandexCallbackCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResponse
            {
                UserId = userId,
                RequiresTenantSetup = true,
                AccessToken = string.Empty,
            });

        // Act
        ActionResult<LoginResponse> result = await BuildController()
            .YandexCallback(new YandexCallbackCommand { Code = "c", RedirectUri = "https://x.ru" }, CancellationToken.None);

        // Assert
        OkObjectResult okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        LoginResponse response = okResult.Value.Should().BeOfType<LoginResponse>().Subject;
        response.RequiresTenantSetup.Should().BeTrue();
        response.AccessToken.Should().BeEmpty();
        response.UserId.Should().Be(userId);
    }

    [Fact]
    public async Task YandexCallback_MediatorCalledExactlyOnce()
    {
        // Arrange
        _mediator
            .Setup(m => m.Send(It.IsAny<YandexCallbackCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResponse { UserId = Guid.NewGuid(), RequiresTenantSetup = false, AccessToken = "t" });

        // Act
        await BuildController()
            .YandexCallback(new YandexCallbackCommand { Code = "c", RedirectUri = "https://x.ru" }, CancellationToken.None);

        // Assert
        _mediator.Verify(m => m.Send(It.IsAny<YandexCallbackCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Google callback ───────────────────────────────────────────────────────

    [Fact]
    public async Task GoogleCallback_ValidCommand_ReturnsOkWithLoginResponse()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();

        LoginResponse handlerResponse = new LoginResponse
        {
            AccessToken = "google-jwt-token",
            UserId = userId,
            TenantId = tenantId,
            RequiresTenantSetup = false,
        };

        _mediator
            .Setup(m => m.Send(It.IsAny<GoogleCallbackCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(handlerResponse);

        GoogleCallbackCommand command = new GoogleCallbackCommand
        {
            Code = "google-code-xyz",
            RedirectUri = "https://app.example.com/auth/google/callback",
        };

        // Act
        ActionResult<LoginResponse> result = await BuildController().GoogleCallback(command, CancellationToken.None);

        // Assert
        OkObjectResult okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        LoginResponse response = okResult.Value.Should().BeOfType<LoginResponse>().Subject;
        response.AccessToken.Should().Be("google-jwt-token");
        response.UserId.Should().Be(userId);
        response.TenantId.Should().Be(tenantId);
        response.RequiresTenantSetup.Should().BeFalse();
    }

    [Fact]
    public async Task GoogleCallback_SendsCommandWithCorrectCodeAndRedirectUri()
    {
        // Arrange
        _mediator
            .Setup(m => m.Send(It.IsAny<GoogleCallbackCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResponse { UserId = Guid.NewGuid(), RequiresTenantSetup = true, AccessToken = string.Empty });

        GoogleCallbackCommand command = new GoogleCallbackCommand
        {
            Code = "google-exact-code",
            RedirectUri = "https://myapp.ru/auth/google/callback",
        };

        // Act
        await BuildController().GoogleCallback(command, CancellationToken.None);

        // Assert
        _mediator.Verify(m => m.Send(
            It.Is<GoogleCallbackCommand>(c =>
                c.Code == "google-exact-code" &&
                c.RedirectUri == "https://myapp.ru/auth/google/callback"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GoogleCallback_RequiresTenantSetup_ReturnsOkWithFlag()
    {
        // Arrange
        Guid userId = Guid.NewGuid();

        _mediator
            .Setup(m => m.Send(It.IsAny<GoogleCallbackCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResponse
            {
                UserId = userId,
                RequiresTenantSetup = true,
                AccessToken = string.Empty,
            });

        // Act
        ActionResult<LoginResponse> result = await BuildController()
            .GoogleCallback(new GoogleCallbackCommand { Code = "c", RedirectUri = "https://x.ru" }, CancellationToken.None);

        // Assert
        OkObjectResult okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        LoginResponse response = okResult.Value.Should().BeOfType<LoginResponse>().Subject;
        response.RequiresTenantSetup.Should().BeTrue();
        response.AccessToken.Should().BeEmpty();
        response.UserId.Should().Be(userId);
    }

    [Fact]
    public async Task GoogleCallback_MediatorCalledExactlyOnce()
    {
        // Arrange
        _mediator
            .Setup(m => m.Send(It.IsAny<GoogleCallbackCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResponse { UserId = Guid.NewGuid(), RequiresTenantSetup = false, AccessToken = "t" });

        // Act
        await BuildController()
            .GoogleCallback(new GoogleCallbackCommand { Code = "c", RedirectUri = "https://x.ru" }, CancellationToken.None);

        // Assert
        _mediator.Verify(m => m.Send(It.IsAny<GoogleCallbackCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Provider isolation ────────────────────────────────────────────────────

    [Fact]
    public async Task YandexCallback_DoesNotDispatchGoogleCallbackCommand()
    {
        // Arrange
        _mediator
            .Setup(m => m.Send(It.IsAny<YandexCallbackCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResponse { UserId = Guid.NewGuid(), RequiresTenantSetup = false, AccessToken = "t" });

        // Act
        await BuildController()
            .YandexCallback(new YandexCallbackCommand { Code = "c", RedirectUri = "https://x.ru" }, CancellationToken.None);

        // Assert
        _mediator.Verify(m => m.Send(It.IsAny<GoogleCallbackCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GoogleCallback_DoesNotDispatchYandexCallbackCommand()
    {
        // Arrange
        _mediator
            .Setup(m => m.Send(It.IsAny<GoogleCallbackCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResponse { UserId = Guid.NewGuid(), RequiresTenantSetup = false, AccessToken = "t" });

        // Act
        await BuildController()
            .GoogleCallback(new GoogleCallbackCommand { Code = "c", RedirectUri = "https://x.ru" }, CancellationToken.None);

        // Assert
        _mediator.Verify(m => m.Send(It.IsAny<YandexCallbackCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
