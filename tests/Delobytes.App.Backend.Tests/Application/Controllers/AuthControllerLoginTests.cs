using System.Text.Json;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Identity.Api.Controllers;
using Delobytes.App.Backend.Identity.Application.Commands;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace Delobytes.App.Backend.Tests.Controllers;

/// <summary>
/// Tests for the local email/password login endpoint.
/// </summary>
public class AuthControllerLoginTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<ILogger<AuthController>> _logger = new();
    private readonly Mock<IUserContext> _userContext = new();
    private readonly Mock<ITenantContext> _tenantContext = new();

    private AuthController BuildController() =>
        new AuthController(_mediator.Object, _logger.Object, _userContext.Object, _tenantContext.Object);

    [Fact]
    public async Task Login_MapsRequestToPasswordLoginCommand()
    {
        // Arrange
        LoginResponse expected = new LoginResponse
        {
            AccessToken = "jwt",
            UserId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            RequiresTenantSetup = false,
        };

        _mediator
            .Setup(m => m.Send(It.IsAny<LoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        LoginRequest request = new LoginRequest
        {
            Email = "user@example.com",
            Password = "secret",
        };

        // Act
        ActionResult<LoginResponse> result = await BuildController()
            .Login(request, CancellationToken.None);

        // Assert
        _mediator.Verify(
            m => m.Send(
                It.Is<LoginCommand>(c => c.Email == "user@example.com" && c.Password == "secret"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        OkObjectResult okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().BeSameAs(expected);
    }

    [Fact]
    public void LoginRequest_DeserializationWithExternalIdentityFields_KeepsOnlyEmailAndPassword()
    {
        // Arrange
        const string json = """
            {
              "email": "user@example.com",
              "password": "secret",
              "identityProvider": "YandexID",
              "externalId": "123456789"
            }
            """;

        // Act
        LoginRequest? request = JsonSerializer.Deserialize<LoginRequest>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        // Assert
        request.Should().NotBeNull();
        request!.Email.Should().Be("user@example.com");
        request.Password.Should().Be("secret");
    }

    [Fact]
    public async Task ChangePassword_AuthenticatedUser_SendsCommandWithUserIdFromContext()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        _userContext.SetupGet(c => c.UserId).Returns(userId);
        AuthController controller = BuildController();

        ChangePasswordRequest request = new ChangePasswordRequest
        {
            CurrentPassword = "old-password",
            NewPassword = "new-password",
        };

        // Act
        IActionResult result = await controller.ChangePassword(request, CancellationToken.None);

        // Assert
        _mediator.Verify(
            m => m.Send(
                It.Is<ChangePasswordCommand>(c =>
                    c.UserId == userId
                    && c.CurrentPassword == "old-password"
                    && c.NewPassword == "new-password"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task ChangePassword_WithoutUser_ThrowsUnauthorized()
    {
        // Arrange
        _userContext.SetupGet(c => c.UserId).Returns((Guid?)null);
        AuthController controller = BuildController();

        // Act
        Func<Task> act = () => controller.ChangePassword(
            new ChangePasswordRequest { CurrentPassword = "old", NewPassword = "new" },
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _mediator.Verify(
            m => m.Send(It.IsAny<ChangePasswordCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
