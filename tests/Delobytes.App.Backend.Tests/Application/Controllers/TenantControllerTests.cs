using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Identity.Api.Controllers;
using Delobytes.App.Backend.Identity.Application.Commands;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Controllers;

public class TenantControllerTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<IUserContext> _userContextMock;
    private readonly Mock<ITenantContext> _tenantContextMock;
    private readonly TenantController _controller;

    public TenantControllerTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _userContextMock = new Mock<IUserContext>();
        _tenantContextMock = new Mock<ITenantContext>();
        _controller = new TenantController(
            _mediatorMock.Object,
            _userContextMock.Object,
            _tenantContextMock.Object);
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
    public async Task CreateTenantForUser_WithoutUserId_ThrowsUnauthorized()
    {
        // Arrange
        CreateTenantForUserRequest request = new CreateTenantForUserRequest
        {
            TenantName = "Test Company"
        };

        _userContextMock.SetupGet(c => c.UserId).Returns((Guid?)null);

        // Act
        Func<Task> act = () => _controller.CreateTenantForUser(
            request,
            CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(act);

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
        _userContextMock.SetupGet(c => c.UserId).Returns(userId);
        _tenantContextMock.SetupGet(c => c.TenantId).Returns(tenantId);
    }
}
