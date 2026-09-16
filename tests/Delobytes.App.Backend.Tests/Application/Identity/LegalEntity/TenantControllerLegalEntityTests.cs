using System.Security.Claims;
using Delobytes.App.Backend.Controllers;
using Delobytes.App.Backend.Identity.Application.Commands.UpdateTenantLegalEntity;
using Delobytes.App.Backend.Identity.Application.Queries.GetTenantLegalEntity;
using Delobytes.App.Backend.Identity.Domain.Enums;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity.LegalEntity;

/// <summary>
/// Tests for the legal entity endpoints of TenantController (stage 10).
/// The tenant is always resolved from the JWT claim, never from the request body,
/// so a client cannot read or modify another tenant's settings.
/// </summary>
public class TenantControllerLegalEntityTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly TenantController _controller;

    public TenantControllerLegalEntityTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _controller = new TenantController(_mediatorMock.Object);
    }

    private void SetupUserClaims(Guid? tenantId, string? rawTenantIdClaim = null, string? role = null)
    {
        List<Claim> claims = new List<Claim>
        {
            new Claim("sub", Guid.NewGuid().ToString()),
        };

        if (tenantId.HasValue)
        {
            claims.Add(new Claim("tenantId", tenantId.Value.ToString()));
        }
        else if (rawTenantIdClaim != null)
        {
            claims.Add(new Claim("tenantId", rawTenantIdClaim));
        }

        if (role != null)
        {
            claims.Add(new Claim("role", role));
        }

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims)),
            },
        };
    }

    // ── GET /api/tenant/legal-entity ──────────────────────────────────────────────

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
            TaxType = TaxType.Usn,
            TaxRatePercent = 6m,
            VatType = VatType.None,
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetTenantLegalEntityQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mediatorResponse);

        SetupUserClaims(tenantId, role: "Administrator");

        // Act
        ActionResult<GetTenantLegalEntityResponse> result =
            await _controller.GetTenantLegalEntity(CancellationToken.None);

        // Assert
        OkObjectResult okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        GetTenantLegalEntityResponse response =
            okResult.Value.Should().BeOfType<GetTenantLegalEntityResponse>().Subject;

        response.TenantId.Should().Be(tenantId);
        response.TaxType.Should().Be(TaxType.Usn);
        response.TaxRatePercent.Should().Be(6m);
        response.VatType.Should().Be(VatType.None);
    }

    [Fact]
    public async Task GetTenantLegalEntity_SendsQueryForTenantFromClaim()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetTenantLegalEntityQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetTenantLegalEntityResponse { TenantId = tenantId });

        SetupUserClaims(tenantId, role: "Manager");

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
    public async Task GetTenantLegalEntity_WithoutTenantClaim_ReturnsUnauthorized()
    {
        // Arrange
        SetupUserClaims(null, role: "Administrator");

        // Act
        ActionResult<GetTenantLegalEntityResponse> result =
            await _controller.GetTenantLegalEntity(CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<UnauthorizedResult>();

        _mediatorMock.Verify(
            m => m.Send(It.IsAny<GetTenantLegalEntityQuery>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetTenantLegalEntity_WithMalformedTenantClaim_ReturnsUnauthorized()
    {
        // Arrange
        SetupUserClaims(null, rawTenantIdClaim: "not-a-guid", role: "Administrator");

        // Act
        ActionResult<GetTenantLegalEntityResponse> result =
            await _controller.GetTenantLegalEntity(CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<UnauthorizedResult>();
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

        SetupUserClaims(tenantId, role: "ReadOnly");

        // Act
        ActionResult<GetTenantLegalEntityResponse> result =
            await _controller.GetTenantLegalEntity(CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
    }

    // ── PATCH /api/tenant/legal-entity ────────────────────────────────────────────

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
            TaxType = TaxType.Usn,
            TaxRatePercent = 6m,
            VatType = VatType.None,
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<UpdateTenantLegalEntityCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mediatorResponse);

        UpdateTenantLegalEntityRequest request = new UpdateTenantLegalEntityRequest
        {
            LegalName = "ООО «Ромашка»",
            Inn = "7712345678",
            TaxType = TaxType.Usn,
            TaxRatePercent = 6m,
            VatType = VatType.None,
        };

        SetupUserClaims(tenantId, role: "Administrator");

        // Act
        ActionResult<UpdateTenantLegalEntityResponse> result =
            await _controller.UpdateTenantLegalEntity(request, CancellationToken.None);

        // Assert
        OkObjectResult okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        UpdateTenantLegalEntityResponse response =
            okResult.Value.Should().BeOfType<UpdateTenantLegalEntityResponse>().Subject;

        response.TenantId.Should().Be(tenantId);
        response.TaxType.Should().Be(TaxType.Usn);
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
            TaxType = TaxType.Osno,
            TaxRatePercent = 20.5m,
            VatType = VatType.Seven,
        };

        SetupUserClaims(tenantId, role: "Administrator");

        // Act
        await _controller.UpdateTenantLegalEntity(request, CancellationToken.None);

        // Assert
        _mediatorMock.Verify(
            m => m.Send(
                It.Is<UpdateTenantLegalEntityCommand>(c =>
                    c.TenantId == tenantId &&
                    c.LegalName == "ООО «Лютик»" &&
                    c.Inn == "771234567890" &&
                    c.TaxType == TaxType.Osno &&
                    c.TaxRatePercent == 20.5m &&
                    c.VatType == VatType.Seven),
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
            TaxType = TaxType.Usn,
            TaxRatePercent = 6m,
            VatType = VatType.None,
        };

        SetupUserClaims(claimTenantId, role: "Administrator");

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
    public async Task UpdateTenantLegalEntity_WithoutTenantClaim_ReturnsUnauthorized()
    {
        // Arrange
        UpdateTenantLegalEntityRequest request = new UpdateTenantLegalEntityRequest
        {
            TaxType = TaxType.Usn,
            TaxRatePercent = 6m,
            VatType = VatType.None,
        };

        SetupUserClaims(null, role: "Administrator");

        // Act
        ActionResult<UpdateTenantLegalEntityResponse> result =
            await _controller.UpdateTenantLegalEntity(request, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<UnauthorizedResult>();

        _mediatorMock.Verify(
            m => m.Send(It.IsAny<UpdateTenantLegalEntityCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateTenantLegalEntity_WithMalformedTenantClaim_ReturnsUnauthorized()
    {
        // Arrange
        UpdateTenantLegalEntityRequest request = new UpdateTenantLegalEntityRequest
        {
            TaxType = TaxType.Usn,
            TaxRatePercent = 6m,
            VatType = VatType.None,
        };

        SetupUserClaims(null, rawTenantIdClaim: "not-a-guid", role: "Administrator");

        // Act
        ActionResult<UpdateTenantLegalEntityResponse> result =
            await _controller.UpdateTenantLegalEntity(request, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<UnauthorizedResult>();
    }
}
