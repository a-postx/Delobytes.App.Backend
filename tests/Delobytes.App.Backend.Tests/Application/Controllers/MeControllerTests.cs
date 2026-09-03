using System.Security.Claims;
using System.Threading.Tasks;
using Delobytes.App.Backend.Controllers;
using Delobytes.App.Backend.Identity.Application.Queries.GetCurrentUser;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Delobytes.App.Backend.Tests.Controllers;

/// <summary>
/// Tests for MeController.
/// </summary>
public class MeControllerTests
{
    private readonly Mock<IMediator> _mediator;

    public MeControllerTests()
    {
        _mediator = new Mock<IMediator>();
    }

    private UserController BuildController(ClaimsPrincipal user)
    {
        UserController controller = new UserController(_mediator.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user },
        };
        return controller;
    }

    private static ClaimsPrincipal BuildPrincipal(string? userId, string? tenantId)
    {
        List<Claim> claims = new List<Claim>();

        if (userId != null)
        {
            claims.Add(new Claim("userId", userId));
        }

        if (tenantId != null)
        {
            claims.Add(new Claim("tenantId", tenantId));
        }

        ClaimsIdentity identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task GetMe_ValidClaims_ReturnsOkWithCurrentUser()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();

        GetCurrentUserResponse handlerResponse = new GetCurrentUserResponse
        {
            UserId = userId,
            Email = "user@example.com",
            DisplayName = "Test User",
            TenantId = tenantId,
            TenantName = "Acme Corp",
        };

        _mediator
            .Setup(m => m.Send(
                It.Is<GetCurrentUserQuery>(q => q.UserId == userId && q.TenantId == tenantId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(handlerResponse);

        UserController controller = BuildController(BuildPrincipal(userId.ToString(), tenantId.ToString()));

        // Act
        ActionResult<GetCurrentUserResponse> result = await controller.GetMe(CancellationToken.None);

        // Assert
        OkObjectResult okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        GetCurrentUserResponse response = okResult.Value.Should().BeOfType<GetCurrentUserResponse>().Subject;
        response.UserId.Should().Be(userId);
        response.TenantName.Should().Be("Acme Corp");
        response.Email.Should().Be("user@example.com");
    }

    [Fact]
    public async Task GetMe_MissingUserIdClaim_ReturnsUnauthorized()
    {
        // Arrange — no userId claim
        UserController controller = BuildController(BuildPrincipal(userId: null, tenantId: Guid.NewGuid().ToString()));

        // Act
        ActionResult<GetCurrentUserResponse> result = await controller.GetMe(CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<UnauthorizedResult>();
        _mediator.Verify(m => m.Send(It.IsAny<GetCurrentUserQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetMe_MissingTenantIdClaim_ReturnsUnauthorized()
    {
        // Arrange — no tenantId claim
        UserController controller = BuildController(BuildPrincipal(userId: Guid.NewGuid().ToString(), tenantId: null));

        // Act
        ActionResult<GetCurrentUserResponse> result = await controller.GetMe(CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<UnauthorizedResult>();
        _mediator.Verify(m => m.Send(It.IsAny<GetCurrentUserQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetMe_InvalidUserIdFormat_ReturnsUnauthorized()
    {
        // Arrange — userId is not a valid Guid
        UserController controller = BuildController(BuildPrincipal(userId: "not-a-guid", tenantId: Guid.NewGuid().ToString()));

        // Act
        ActionResult<GetCurrentUserResponse> result = await controller.GetMe(CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<UnauthorizedResult>();
        _mediator.Verify(m => m.Send(It.IsAny<GetCurrentUserQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetMe_InvalidTenantIdFormat_ReturnsUnauthorized()
    {
        // Arrange — tenantId is not a valid Guid
        UserController controller = BuildController(BuildPrincipal(userId: Guid.NewGuid().ToString(), tenantId: "not-a-guid"));

        // Act
        ActionResult<GetCurrentUserResponse> result = await controller.GetMe(CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<UnauthorizedResult>();
        _mediator.Verify(m => m.Send(It.IsAny<GetCurrentUserQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetMe_SendsQueryWithClaimValues()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();

        _mediator
            .Setup(m => m.Send(It.IsAny<GetCurrentUserQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetCurrentUserResponse
            {
                UserId = userId,
                Email = "u@u.com",
                TenantId = tenantId,
                TenantName = "T",
            });

        UserController controller = BuildController(BuildPrincipal(userId.ToString(), tenantId.ToString()));

        // Act
        await controller.GetMe(CancellationToken.None);

        // Assert
        _mediator.Verify(m => m.Send(
            It.Is<GetCurrentUserQuery>(q => q.UserId == userId && q.TenantId == tenantId),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
