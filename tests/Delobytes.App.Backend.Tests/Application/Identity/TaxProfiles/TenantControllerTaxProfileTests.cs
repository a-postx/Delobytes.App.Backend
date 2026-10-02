using System.Security.Claims;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Controllers;
using Delobytes.App.Backend.Identity.Application.Commands.TaxProfiles.CreateTenantTaxProfile;
using Delobytes.App.Backend.Identity.Application.Commands.TaxProfiles.DeleteTenantTaxProfile;
using Delobytes.App.Backend.Identity.Application.Queries.TaxProfiles.GetActiveTenantTaxProfile;
using Delobytes.App.Backend.Identity.Application.Queries.TaxProfiles.GetTenantTaxProfiles;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity.TaxProfiles;

/// <summary>
/// Tests for the tax profile endpoints of TenantController.
///
/// The tenant always comes from the JWT claim, never from the request body or route, and
/// the status codes are part of the contract: 201 on create, 409 on a ValidFrom conflict
/// or on deleting a non-latest version, 404 when the profile is absent or foreign, and
/// 401 when the tenant claim is missing.
/// </summary>
public class TenantControllerTaxProfileTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly TenantController _controller;

    public TenantControllerTaxProfileTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _controller = new TenantController(_mediatorMock.Object);
    }

    private void SetupUserClaims(Guid? tenantId, string? rawTenantIdClaim = null, string? role = "Administrator")
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

    // ── GET /api/tenant/tax-profiles ──────────────────────────────────────────

    [Fact]
    public async Task GetTaxProfiles_ValidTenantClaim_ReturnsOkWithList()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();

        GetTenantTaxProfilesResponse mediatorResponse = new GetTenantTaxProfilesResponse
        {
            Items = new List<TenantTaxProfileItem>
            {
                new TenantTaxProfileItem
                {
                    Id = Guid.NewGuid(),
                    Regime = TaxRegime.UsnIncome,
                    RatePercent = 6m,
                    Vat = VatType.None,
                    ValidFrom = new DateOnly(2026, 1, 1),
                    CreatedAt = DateTimeOffset.UtcNow,
                },
            },
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetTenantTaxProfilesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mediatorResponse);

        SetupUserClaims(tenantId);

        // Act
        ActionResult<GetTenantTaxProfilesResponse> result =
            await _controller.GetTaxProfiles(CancellationToken.None);

        // Assert
        OkObjectResult okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        GetTenantTaxProfilesResponse response =
            okResult.Value.Should().BeOfType<GetTenantTaxProfilesResponse>().Subject;

        response.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task GetTaxProfiles_SendsQueryForTenantFromClaim()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetTenantTaxProfilesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetTenantTaxProfilesResponse());

        SetupUserClaims(tenantId, role: "ReadOnly");

        // Act
        await _controller.GetTaxProfiles(CancellationToken.None);

        // Assert
        _mediatorMock.Verify(
            m => m.Send(
                It.Is<GetTenantTaxProfilesQuery>(q => q.TenantId == tenantId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetTaxProfiles_WithoutTenantClaim_ReturnsUnauthorized()
    {
        // Arrange
        SetupUserClaims(null);

        // Act
        ActionResult<GetTenantTaxProfilesResponse> result =
            await _controller.GetTaxProfiles(CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<UnauthorizedResult>();

        _mediatorMock.Verify(
            m => m.Send(It.IsAny<GetTenantTaxProfilesQuery>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetTaxProfiles_WithMalformedTenantClaim_ReturnsUnauthorized()
    {
        // Arrange
        SetupUserClaims(null, rawTenantIdClaim: "not-a-guid");

        // Act
        ActionResult<GetTenantTaxProfilesResponse> result =
            await _controller.GetTaxProfiles(CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<UnauthorizedResult>();
    }

    // ── GET /api/tenant/tax-profiles/active ───────────────────────────────────

    [Fact]
    public async Task GetActiveTaxProfile_ProfileExists_ReturnsOkWithFoundTrue()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        DateTimeOffset at = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

        GetActiveTenantTaxProfileResponse mediatorResponse = new GetActiveTenantTaxProfileResponse
        {
            Found = true,
            Id = Guid.NewGuid(),
            Regime = TaxRegime.UsnIncome,
            RatePercent = 6m,
            Vat = VatType.None,
            ValidFrom = new DateOnly(2026, 1, 1),
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetActiveTenantTaxProfileQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mediatorResponse);

        SetupUserClaims(tenantId);

        // Act
        ActionResult<GetActiveTenantTaxProfileResponse> result =
            await _controller.GetActiveTaxProfile(at, CancellationToken.None);

        // Assert
        OkObjectResult okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        GetActiveTenantTaxProfileResponse response =
            okResult.Value.Should().BeOfType<GetActiveTenantTaxProfileResponse>().Subject;

        response.Found.Should().BeTrue();
        response.RatePercent.Should().Be(6m);
    }

    [Fact]
    public async Task GetActiveTaxProfile_NoProfile_ReturnsOkWithFoundFalseNotNotFound()
    {
        // Arrange
        // A tenant that has not configured tax yet is a normal state, so the endpoint
        // answers 200 with found = false; a 404 would make the UI treat it as an error.
        Guid tenantId = Guid.NewGuid();

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetActiveTenantTaxProfileQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetActiveTenantTaxProfileResponse { Found = false });

        SetupUserClaims(tenantId);

        // Act
        ActionResult<GetActiveTenantTaxProfileResponse> result =
            await _controller.GetActiveTaxProfile(null, CancellationToken.None);

        // Assert
        OkObjectResult okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        GetActiveTenantTaxProfileResponse response =
            okResult.Value.Should().BeOfType<GetActiveTenantTaxProfileResponse>().Subject;

        response.Found.Should().BeFalse();
    }

    [Fact]
    public async Task GetActiveTaxProfile_PassesTheMomentThroughToTheQuery()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        DateTimeOffset at = new DateTimeOffset(2026, 1, 1, 0, 30, 0, TimeSpan.FromHours(3));

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetActiveTenantTaxProfileQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetActiveTenantTaxProfileResponse { Found = false });

        SetupUserClaims(tenantId);

        // Act
        await _controller.GetActiveTaxProfile(at, CancellationToken.None);

        // Assert
        _mediatorMock.Verify(
            m => m.Send(
                It.Is<GetActiveTenantTaxProfileQuery>(q => q.TenantId == tenantId && q.At == at),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetActiveTaxProfile_WithoutTenantClaim_ReturnsUnauthorized()
    {
        // Arrange
        SetupUserClaims(null);

        // Act
        ActionResult<GetActiveTenantTaxProfileResponse> result =
            await _controller.GetActiveTaxProfile(null, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<UnauthorizedResult>();
    }

    // ── POST /api/tenant/tax-profiles ─────────────────────────────────────────

    [Fact]
    public async Task CreateTaxProfile_ValidRequest_ReturnsCreatedAndMapsEveryFieldToCommand()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid createdId = Guid.NewGuid();
        DateOnly validFrom = new DateOnly(2026, 6, 1);

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CreateTenantTaxProfileCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateTenantTaxProfileResponse { Id = createdId });

        CreateTenantTaxProfileRequest request = new CreateTenantTaxProfileRequest
        {
            Regime = TaxRegime.UsnIncome,
            RatePercent = 6m,
            Vat = VatType.None,
            ValidFrom = validFrom,
        };

        SetupUserClaims(tenantId);

        // Act
        ActionResult<CreateTenantTaxProfileResponse> result =
            await _controller.CreateTaxProfile(request, CancellationToken.None);

        // Assert
        CreatedAtActionResult created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        CreateTenantTaxProfileResponse response =
            created.Value.Should().BeOfType<CreateTenantTaxProfileResponse>().Subject;
        response.Id.Should().Be(createdId);

        _mediatorMock.Verify(
            m => m.Send(
                It.Is<CreateTenantTaxProfileCommand>(c =>
                    c.TenantId == tenantId &&
                    c.Regime == TaxRegime.UsnIncome &&
                    c.RatePercent == 6m &&
                    c.Vat == VatType.None &&
                    c.ValidFrom == validFrom),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateTaxProfile_ValidFromConflict_ReturnsConflict()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CreateTenantTaxProfileCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateTenantTaxProfileResponse { Conflict = true });

        CreateTenantTaxProfileRequest request = new CreateTenantTaxProfileRequest
        {
            Regime = TaxRegime.UsnIncome,
            RatePercent = 6m,
            Vat = VatType.None,
            ValidFrom = new DateOnly(2026, 1, 1),
        };

        SetupUserClaims(tenantId);

        // Act
        ActionResult<CreateTenantTaxProfileResponse> result =
            await _controller.CreateTaxProfile(request, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task CreateTaxProfile_TenantIdComesFromClaimNotFromBody()
    {
        // Arrange
        Guid claimTenantId = Guid.NewGuid();

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CreateTenantTaxProfileCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateTenantTaxProfileResponse { Id = Guid.NewGuid() });

        CreateTenantTaxProfileRequest request = new CreateTenantTaxProfileRequest
        {
            Regime = TaxRegime.UsnIncome,
            RatePercent = 6m,
            Vat = VatType.None,
            ValidFrom = new DateOnly(2026, 1, 1),
        };

        SetupUserClaims(claimTenantId);

        // Act
        await _controller.CreateTaxProfile(request, CancellationToken.None);

        // Assert
        _mediatorMock.Verify(
            m => m.Send(
                It.Is<CreateTenantTaxProfileCommand>(c => c.TenantId == claimTenantId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateTaxProfile_WithoutTenantClaim_ReturnsUnauthorized()
    {
        // Arrange
        CreateTenantTaxProfileRequest request = new CreateTenantTaxProfileRequest
        {
            Regime = TaxRegime.UsnIncome,
            RatePercent = 6m,
            Vat = VatType.None,
            ValidFrom = new DateOnly(2026, 1, 1),
        };

        SetupUserClaims(null);

        // Act
        ActionResult<CreateTenantTaxProfileResponse> result =
            await _controller.CreateTaxProfile(request, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<UnauthorizedResult>();

        _mediatorMock.Verify(
            m => m.Send(It.IsAny<CreateTenantTaxProfileCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── DELETE /api/tenant/tax-profiles/{id} ──────────────────────────────────

    [Fact]
    public async Task DeleteTaxProfile_LatestVersion_ReturnsOk()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid profileId = Guid.NewGuid();

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<DeleteTenantTaxProfileCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteTenantTaxProfileResponse { Found = true });

        SetupUserClaims(tenantId);

        // Act
        ActionResult<DeleteTenantTaxProfileResponse> result =
            await _controller.DeleteTaxProfile(profileId, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();

        _mediatorMock.Verify(
            m => m.Send(
                It.Is<DeleteTenantTaxProfileCommand>(c => c.TenantId == tenantId && c.Id == profileId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteTaxProfile_ProfileNotFoundOrForeign_ReturnsNotFound()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<DeleteTenantTaxProfileCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteTenantTaxProfileResponse { Found = false });

        SetupUserClaims(tenantId);

        // Act
        ActionResult<DeleteTenantTaxProfileResponse> result =
            await _controller.DeleteTaxProfile(Guid.NewGuid(), CancellationToken.None);

        // Assert
        NotFoundObjectResult notFound = result.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFound.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteTaxProfile_NonLatestVersion_ReturnsConflict()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<DeleteTenantTaxProfileCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteTenantTaxProfileResponse { Found = true, NotLatest = true });

        SetupUserClaims(tenantId);

        // Act
        ActionResult<DeleteTenantTaxProfileResponse> result =
            await _controller.DeleteTaxProfile(Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task DeleteTaxProfile_AlreadyEffectiveVersion_ReturnsConflict()
    {
        // Arrange
        // Even the latest (and possibly only) version must be refused once its ValidFrom
        // has arrived: it may already be used in reports.
        Guid tenantId = Guid.NewGuid();

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<DeleteTenantTaxProfileCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteTenantTaxProfileResponse { Found = true, AlreadyEffective = true });

        SetupUserClaims(tenantId);

        // Act
        ActionResult<DeleteTenantTaxProfileResponse> result =
            await _controller.DeleteTaxProfile(Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task DeleteTaxProfile_WithoutTenantClaim_ReturnsUnauthorized()
    {
        // Arrange
        SetupUserClaims(null);

        // Act
        ActionResult<DeleteTenantTaxProfileResponse> result =
            await _controller.DeleteTaxProfile(Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<UnauthorizedResult>();

        _mediatorMock.Verify(
            m => m.Send(It.IsAny<DeleteTenantTaxProfileCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
