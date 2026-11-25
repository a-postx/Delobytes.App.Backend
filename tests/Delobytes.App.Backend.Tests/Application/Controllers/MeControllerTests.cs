using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Identity.Api.Controllers;
using Delobytes.App.Backend.Identity.Application.Queries;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Delobytes.App.Backend.Tests.Controllers;

/// <summary>
/// Tests for the current-user endpoint. Claim parsing itself is covered by
/// <see cref="Infrastructure.UserContextTests"/> and
/// <see cref="Infrastructure.CompositeTenantContextTests"/>; here the controller contract is verified.
/// </summary>
public class MeControllerTests
{
    private readonly Mock<IMediator> _mediator;
    private readonly Mock<IUserContext> _userContext;
    private readonly Mock<ITenantContext> _tenantContext;

    public MeControllerTests()
    {
        _mediator = new Mock<IMediator>();
        _userContext = new Mock<IUserContext>();
        _tenantContext = new Mock<ITenantContext>();
    }

    private UserController BuildController(Guid? userId, Guid? tenantId)
    {
        _userContext.SetupGet(c => c.UserId).Returns(userId);
        _tenantContext.SetupGet(c => c.TenantId).Returns(tenantId);

        return new UserController(_mediator.Object, _userContext.Object, _tenantContext.Object);
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

        UserController controller = BuildController(userId, tenantId);

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
    public async Task GetMe_MissingUserId_ThrowsUnauthorized()
    {
        // Arrange
        UserController controller = BuildController(userId: null, tenantId: Guid.NewGuid());

        // Act
        Func<Task> act = () => controller.GetMe(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _mediator.Verify(m => m.Send(It.IsAny<GetCurrentUserQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetMe_MissingTenantId_ThrowsUnauthorized()
    {
        // Arrange
        UserController controller = BuildController(userId: Guid.NewGuid(), tenantId: null);

        // Act
        Func<Task> act = () => controller.GetMe(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _mediator.Verify(m => m.Send(It.IsAny<GetCurrentUserQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetMe_SendsQueryWithContextValues()
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

        UserController controller = BuildController(userId, tenantId);

        // Act
        await controller.GetMe(CancellationToken.None);

        // Assert
        _mediator.Verify(m => m.Send(
            It.Is<GetCurrentUserQuery>(q => q.UserId == userId && q.TenantId == tenantId),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
