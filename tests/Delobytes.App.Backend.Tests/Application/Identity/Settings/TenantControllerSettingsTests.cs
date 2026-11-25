using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Identity.Api.Controllers;
using Delobytes.App.Backend.Identity.Application.Commands;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity.Settings;

/// <summary>
/// Tests for the settings endpoint of TenantController.
/// The tenant always comes from the current tenant context, never from the request body,
/// so a client cannot modify another tenant's settings.
/// </summary>
public class TenantControllerSettingsTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<IUserContext> _userContextMock;
    private readonly Mock<ITenantContext> _tenantContextMock;
    private readonly TenantController _controller;

    public TenantControllerSettingsTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _userContextMock = new Mock<IUserContext>();
        _tenantContextMock = new Mock<ITenantContext>();
        _controller = new TenantController(
            _mediatorMock.Object,
            _userContextMock.Object,
            _tenantContextMock.Object);
    }

    private void SetupUserClaims(Guid? tenantId)
    {
        _userContextMock.SetupGet(c => c.UserId).Returns(Guid.NewGuid());
        _tenantContextMock.SetupGet(c => c.TenantId).Returns(tenantId);
    }

    [Fact]
    public async Task UpdateTenantSettings_ValidTenantClaim_ReturnsOkWithSavedZone()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<UpdateTenantSettingsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateTenantSettingsResponse
            {
                TenantId = tenantId,
                TimeZone = "Asia/Vladivostok",
            });

        SetupUserClaims(tenantId);

        // Act
        ActionResult<UpdateTenantSettingsResponse> result = await _controller.UpdateTenantSettings(
            new UpdateTenantSettingsRequest { TimeZone = "Asia/Vladivostok" },
            CancellationToken.None);

        // Assert
        OkObjectResult okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        UpdateTenantSettingsResponse payload = okResult.Value.Should().BeOfType<UpdateTenantSettingsResponse>().Subject;
        payload.TenantId.Should().Be(tenantId);
        payload.TimeZone.Should().Be("Asia/Vladivostok");
    }

    [Fact]
    public async Task UpdateTenantSettings_TenantIdComesFromClaim_NotFromRequestBody()
    {
        // Arrange
        Guid claimTenantId = Guid.NewGuid();
        UpdateTenantSettingsCommand? capturedCommand = null;

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<UpdateTenantSettingsCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<UpdateTenantSettingsResponse>, CancellationToken>((request, _) =>
                capturedCommand = (UpdateTenantSettingsCommand)request)
            .ReturnsAsync(new UpdateTenantSettingsResponse
            {
                TenantId = claimTenantId,
                TimeZone = "Europe/Samara",
            });

        SetupUserClaims(claimTenantId);

        // Act
        await _controller.UpdateTenantSettings(
            new UpdateTenantSettingsRequest { TimeZone = "Europe/Samara" },
            CancellationToken.None);

        // Assert
        capturedCommand.Should().NotBeNull();
        capturedCommand!.TenantId.Should().Be(claimTenantId);
        capturedCommand.TimeZone.Should().Be("Europe/Samara");
    }

    [Fact]
    public async Task UpdateTenantSettings_MissingTenant_ThrowsUnauthorizedWithoutDispatch()
    {
        // Arrange
        SetupUserClaims(null);

        // Act
        Func<Task> act = () => _controller.UpdateTenantSettings(
            new UpdateTenantSettingsRequest { TimeZone = "Europe/Samara" },
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _mediatorMock.Verify(
            m => m.Send(It.IsAny<UpdateTenantSettingsCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
