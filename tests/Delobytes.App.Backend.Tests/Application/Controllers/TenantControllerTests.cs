using System.Security.Claims;
using Delobytes.App.Backend.Controllers;
using Delobytes.App.Backend.Identity.Application.Commands.CreateTenant;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Controllers;

public class TenantControllerTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly TenantController _controller;

    public TenantControllerTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _controller = new TenantController(_mediatorMock.Object);
    }

    [Fact]
    public async Task CreateTenantForUser_WithValidRequest_ReturnsOkWithTenantInfo()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid currentTenantId = Guid.NewGuid();
        Guid newTenantId = Guid.NewGuid();
        string tenantName = "New Company";

        CreateTenantForUserRequest request = new CreateTenantForUserRequest
        {
            TenantName = tenantName
        };

        CreateTenantResponse mediatorResponse = new CreateTenantResponse
        {
            TenantId = newTenantId,
            AccessToken = "jwt-token-not-used"
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CreateTenantCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mediatorResponse);

        SetupUserClaims(userId, currentTenantId);

        // Act
        ActionResult<CreateTenantForUserResponse> result = await _controller.CreateTenantForUser(
            request,
            CancellationToken.None);

        // Assert
        OkObjectResult okResult = Assert.IsType<OkObjectResult>(result.Result);
        CreateTenantForUserResponse response = Assert.IsType<CreateTenantForUserResponse>(okResult.Value);
        
        Assert.Equal(newTenantId, response.TenantId);
        Assert.Equal(tenantName, response.TenantName);

        _mediatorMock.Verify(
            m => m.Send(
                It.Is<CreateTenantCommand>(cmd =>
                    cmd.UserId == userId &&
                    cmd.TenantName == tenantName &&
                    cmd.CurrentTenantId == currentTenantId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateTenantForUser_WithoutCurrentTenant_SendsNullCurrentTenantId()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid newTenantId = Guid.NewGuid();
        string tenantName = "First Company";

        CreateTenantForUserRequest request = new CreateTenantForUserRequest
        {
            TenantName = tenantName
        };

        CreateTenantResponse mediatorResponse = new CreateTenantResponse
        {
            TenantId = newTenantId,
            AccessToken = "jwt-token-not-used"
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CreateTenantCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mediatorResponse);

        SetupUserClaims(userId, null);

        // Act
        ActionResult<CreateTenantForUserResponse> result = await _controller.CreateTenantForUser(
            request,
            CancellationToken.None);

        // Assert
        OkObjectResult okResult = Assert.IsType<OkObjectResult>(result.Result);
        CreateTenantForUserResponse response = Assert.IsType<CreateTenantForUserResponse>(okResult.Value);
        
        Assert.Equal(newTenantId, response.TenantId);
        Assert.Equal(tenantName, response.TenantName);

        _mediatorMock.Verify(
            m => m.Send(
                It.Is<CreateTenantCommand>(cmd =>
                    cmd.UserId == userId &&
                    cmd.TenantName == tenantName &&
                    cmd.CurrentTenantId == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateTenantForUser_WithoutUserIdClaim_ReturnsUnauthorized()
    {
        // Arrange
        CreateTenantForUserRequest request = new CreateTenantForUserRequest
        {
            TenantName = "Test Company"
        };

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity())
            }
        };

        // Act
        ActionResult<CreateTenantForUserResponse> result = await _controller.CreateTenantForUser(
            request,
            CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedResult>(result.Result);

        _mediatorMock.Verify(
            m => m.Send(It.IsAny<CreateTenantCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateTenantForUser_WithInvalidUserIdClaim_ReturnsUnauthorized()
    {
        // Arrange
        CreateTenantForUserRequest request = new CreateTenantForUserRequest
        {
            TenantName = "Test Company"
        };

        ClaimsIdentity identity = new ClaimsIdentity(new[]
        {
            new Claim("userId", "not-a-guid")
        });

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };

        // Act
        ActionResult<CreateTenantForUserResponse> result = await _controller.CreateTenantForUser(
            request,
            CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedResult>(result.Result);

        _mediatorMock.Verify(
            m => m.Send(It.IsAny<CreateTenantCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateTenantForUser_WhenMediatorThrowsException_PropagatesException()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid currentTenantId = Guid.NewGuid();

        CreateTenantForUserRequest request = new CreateTenantForUserRequest
        {
            TenantName = "Test Company"
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CreateTenantCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Только Администратор текущего пространства может создавать дополнительные пространства."));

        SetupUserClaims(userId, currentTenantId);

        // Act & Assert
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _controller.CreateTenantForUser(request, CancellationToken.None));

        Assert.Contains("Администратор", exception.Message);
    }

    private void SetupUserClaims(Guid userId, Guid? tenantId)
    {
        List<Claim> claims = new List<Claim>
        {
            new Claim("userId", userId.ToString())
        };

        if (tenantId.HasValue)
        {
            claims.Add(new Claim("tenantId", tenantId.Value.ToString()));
        }

        ClaimsIdentity identity = new ClaimsIdentity(claims);
        
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
    }
}
