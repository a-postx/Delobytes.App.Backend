using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Identity.Api.Controllers;
using Delobytes.App.Backend.Identity.Application.Commands;
using Delobytes.App.Backend.Identity.Application.Queries;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity.LegalEntity;

/// <summary>
/// Tests for the legal entity endpoints of TenantController.
/// The tenant always comes from the current tenant context, never from the request body,
/// so a client cannot read or modify another tenant's settings.
/// </summary>
public class TenantControllerLegalEntityTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<IUserContext> _userContextMock;
    private readonly Mock<ITenantContext> _tenantContextMock;
    private readonly TenantController _controller;

    public TenantControllerLegalEntityTests()
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

    // ── GET /api/tenant/legal-entity ──────────────────────────────────────────

    [Fact]
    public async Task GetTenantLegalEntity_ValidTenantClaim_ReturnsOkWithSettings()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();

        GetTenantLegalEntityResponse mediatorResponse = new GetTenantLegalEntityResponse
        {
            TenantId = tenantId,
            LegalName = "ООО «Ромашка»",
            Inn = "7712345678",
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetTenantLegalEntityQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mediatorResponse);

        SetupUserClaims(tenantId);

        // Act
        ActionResult<GetTenantLegalEntityResponse> result =
            await _controller.GetTenantLegalEntity(CancellationToken.None);

        // Assert
        OkObjectResult okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        GetTenantLegalEntityResponse response =
            okResult.Value.Should().BeOfType<GetTenantLegalEntityResponse>().Subject;

        response.TenantId.Should().Be(tenantId);
        response.LegalName.Should().Be("ООО «Ромашка»");
        response.Inn.Should().Be("7712345678");
    }

    [Fact]
    public async Task GetTenantLegalEntity_SendsQueryForTenantFromClaim()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetTenantLegalEntityQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetTenantLegalEntityResponse { TenantId = tenantId });

        SetupUserClaims(tenantId);

        // Act
        await _controller.GetTenantLegalEntity(CancellationToken.None);

        // Assert
        _mediatorMock.Verify(
            m => m.Send(
                It.Is<GetTenantLegalEntityQuery>(q => q.TenantId == tenantId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetTenantLegalEntity_WithoutTenant_ThrowsUnauthorized()
    {
        // Arrange
        SetupUserClaims(null);

        // Act
        Func<Task> act = () => _controller.GetTenantLegalEntity(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();

        _mediatorMock.Verify(
            m => m.Send(It.IsAny<GetTenantLegalEntityQuery>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetTenantLegalEntity_NonAdministrator_IsStillAllowedToRead()
    {
        // Arrange – the card is visible to every member, so reading is not
        // restricted to administrators.
        Guid tenantId = Guid.NewGuid();

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetTenantLegalEntityQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetTenantLegalEntityResponse { TenantId = tenantId });

        SetupUserClaims(tenantId);

        // Act
        ActionResult<GetTenantLegalEntityResponse> result =
            await _controller.GetTenantLegalEntity(CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
    }

    // ── PATCH /api/tenant/legal-entity ────────────────────────────────────────

    [Fact]
    public async Task UpdateTenantLegalEntity_ValidRequest_ReturnsOkWithUpdatedSettings()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();

        UpdateTenantLegalEntityResponse mediatorResponse = new UpdateTenantLegalEntityResponse
        {
            TenantId = tenantId,
            LegalName = "ООО «Ромашка»",
            Inn = "7712345678",
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<UpdateTenantLegalEntityCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mediatorResponse);

        UpdateTenantLegalEntityRequest request = new UpdateTenantLegalEntityRequest
        {
            LegalName = "ООО «Ромашка»",
            Inn = "7712345678",
        };

        SetupUserClaims(tenantId);

        // Act
        ActionResult<UpdateTenantLegalEntityResponse> result =
            await _controller.UpdateTenantLegalEntity(request, CancellationToken.None);

        // Assert
        OkObjectResult okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        UpdateTenantLegalEntityResponse response =
            okResult.Value.Should().BeOfType<UpdateTenantLegalEntityResponse>().Subject;

        response.TenantId.Should().Be(tenantId);
        response.LegalName.Should().Be("ООО «Ромашка»");
    }

    [Fact]
    public async Task UpdateTenantLegalEntity_MapsEveryRequestBodyFieldToCommand()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<UpdateTenantLegalEntityCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateTenantLegalEntityResponse { TenantId = tenantId });

        UpdateTenantLegalEntityRequest request = new UpdateTenantLegalEntityRequest
        {
            LegalName = "ООО «Лютик»",
            Inn = "771234567890",
        };

        SetupUserClaims(tenantId);

        // Act
        await _controller.UpdateTenantLegalEntity(request, CancellationToken.None);

        // Assert
        _mediatorMock.Verify(
            m => m.Send(
                It.Is<UpdateTenantLegalEntityCommand>(c =>
                    c.TenantId == tenantId &&
                    c.LegalName == "ООО «Лютик»" &&
                    c.Inn == "771234567890"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateTenantLegalEntity_TenantIdComesFromClaimNotFromBody()
    {
        // Arrange
        Guid claimTenantId = Guid.NewGuid();

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<UpdateTenantLegalEntityCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateTenantLegalEntityResponse { TenantId = claimTenantId });

        UpdateTenantLegalEntityRequest request = new UpdateTenantLegalEntityRequest
        {
            LegalName = "ООО «Ромашка»",
        };

        SetupUserClaims(claimTenantId);

        // Act
        await _controller.UpdateTenantLegalEntity(request, CancellationToken.None);

        // Assert
        _mediatorMock.Verify(
            m => m.Send(
                It.Is<UpdateTenantLegalEntityCommand>(c => c.TenantId == claimTenantId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateTenantLegalEntity_WithoutTenant_ThrowsUnauthorized()
    {
        // Arrange
        UpdateTenantLegalEntityRequest request = new UpdateTenantLegalEntityRequest
        {
            LegalName = "ООО «Ромашка»",
        };

        SetupUserClaims(null);

        // Act
        Func<Task> act = () => _controller.UpdateTenantLegalEntity(request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();

        _mediatorMock.Verify(
            m => m.Send(It.IsAny<UpdateTenantLegalEntityCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
