using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Identity.Api.Controllers;
using Delobytes.App.Backend.Identity.Application.Commands;
using Delobytes.App.Backend.Identity.Application.Models;
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
    private readonly Mock<IUserContext> _userContext;
    private readonly Mock<ITenantContext> _tenantContext;

    public AuthControllerOAuthTests()
    {
        _mediator = new Mock<IMediator>();
        _logger = new Mock<ILogger<AuthController>>();
        _userContext = new Mock<IUserContext>();
        _tenantContext = new Mock<ITenantContext>();
    }

    private AuthController BuildController() =>
        new AuthController(_mediator.Object, _logger.Object, _userContext.Object, _tenantContext.Object);

    private static OAuthCallbackRequest BuildRequest(
        string code = "auth-code",
        string redirectUri = "https://app.example.com/auth/callback")
        => new OAuthCallbackRequest { Code = code, RedirectUri = redirectUri };

    private void SetupSession(LoginResponse response)
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<OAuthCallbackCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
    }

    [Theory]
    [InlineData("yandex")]
    [InlineData("google")]
    public async Task Callback_ValidRequest_ReturnsOkWithLoginResponse(string provider)
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();

        SetupSession(new LoginResponse
        {
            AccessToken = "jwt-token",
            UserId = userId,
            TenantId = tenantId,
            RequiresTenantSetup = false,
        });

        AuthController controller = BuildController();

        // Act
        ActionResult<LoginResponse> result = provider == "yandex"
            ? await controller.YandexCallback(BuildRequest(), CancellationToken.None)
            : await controller.GoogleCallback(BuildRequest(), CancellationToken.None);

        // Assert
        OkObjectResult okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        LoginResponse response = okResult.Value.Should().BeOfType<LoginResponse>().Subject;
        response.AccessToken.Should().Be("jwt-token");
        response.UserId.Should().Be(userId);
        response.TenantId.Should().Be(tenantId);
        response.RequiresTenantSetup.Should().BeFalse();
    }

    [Fact]
    public async Task YandexCallback_SendsCommandForYandexWithCodeAndRedirectUri()
    {
        // Arrange
        SetupSession(new LoginResponse { UserId = Guid.NewGuid(), AccessToken = "t" });

        // Act
        await BuildController().YandexCallback(
            BuildRequest(code: "exact-code-value", redirectUri: "https://myapp.ru/auth/yandex/callback"),
            CancellationToken.None);

        // Assert
        _mediator.Verify(m => m.Send(
            It.Is<OAuthCallbackCommand>(c =>
                c.Provider == OAuthProviderKind.Yandex &&
                c.Code == "exact-code-value" &&
                c.RedirectUri == "https://myapp.ru/auth/yandex/callback"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GoogleCallback_SendsCommandForGoogleWithCodeAndRedirectUri()
    {
        // Arrange
        SetupSession(new LoginResponse { UserId = Guid.NewGuid(), AccessToken = "t" });

        // Act
        await BuildController().GoogleCallback(
            BuildRequest(code: "google-exact-code", redirectUri: "https://myapp.ru/auth/google/callback"),
            CancellationToken.None);

        // Assert
        _mediator.Verify(m => m.Send(
            It.Is<OAuthCallbackCommand>(c =>
                c.Provider == OAuthProviderKind.Google &&
                c.Code == "google-exact-code" &&
                c.RedirectUri == "https://myapp.ru/auth/google/callback"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task YandexCallback_RequiresTenantSetup_ReturnsOkWithFlag()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        SetupSession(new LoginResponse
        {
            UserId = userId,
            RequiresTenantSetup = true,
            AccessToken = "temp-token",
        });

        // Act
        ActionResult<LoginResponse> result = await BuildController()
            .YandexCallback(BuildRequest(), CancellationToken.None);

        // Assert
        OkObjectResult okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        LoginResponse response = okResult.Value.Should().BeOfType<LoginResponse>().Subject;
        response.RequiresTenantSetup.Should().BeTrue();
        response.AccessToken.Should().Be("temp-token");
        response.UserId.Should().Be(userId);
    }
}
