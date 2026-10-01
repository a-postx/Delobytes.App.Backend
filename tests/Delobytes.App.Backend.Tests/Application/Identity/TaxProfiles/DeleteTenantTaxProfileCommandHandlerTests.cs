using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Identity.Application.Commands.TaxProfiles.DeleteTenantTaxProfile;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity.TaxProfiles;

/// <summary>
/// Tests for DeleteTenantTaxProfileCommandHandler.
///
/// Only the latest version may be removed. Deleting an earlier one would leave a gap in
/// history where an even earlier profile silently applies to a period already closed
/// under the deleted one.
/// </summary>
public class DeleteTenantTaxProfileCommandHandlerTests
{
    private readonly Mock<ITenantTaxProfileRepository> _taxProfileRepositoryMock;
    private readonly DeleteTenantTaxProfileCommandHandler _handler;

    public DeleteTenantTaxProfileCommandHandlerTests()
    {
        _taxProfileRepositoryMock = new Mock<ITenantTaxProfileRepository>();
        _handler = new DeleteTenantTaxProfileCommandHandler(_taxProfileRepositoryMock.Object);
    }

    private static TenantTaxProfile BuildProfile(Guid tenantId, DateOnly validFrom) => new TenantTaxProfile
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Regime = TaxRegime.UsnIncome,
        RatePercent = 6m,
        Vat = VatType.None,
        ValidFrom = validFrom,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private void Setup(TenantTaxProfile? found, TenantTaxProfile? latest)
    {
        _taxProfileRepositoryMock
            .Setup(r => r.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(found);

        _taxProfileRepositoryMock
            .Setup(r => r.GetLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(latest);

        _taxProfileRepositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    [Fact]
    public async Task Handle_LatestProfile_IsRemoved()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        TenantTaxProfile latest = BuildProfile(tenantId, new DateOnly(2026, 6, 1));
        Setup(latest, latest);

        DeleteTenantTaxProfileCommand command = new DeleteTenantTaxProfileCommand
        {
            TenantId = tenantId,
            Id = latest.Id,
        };

        // Act
        DeleteTenantTaxProfileResponse response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        response.NotLatest.Should().BeFalse();

        _taxProfileRepositoryMock.Verify(r => r.Remove(latest), Times.Once);
        _taxProfileRepositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_EarlierProfile_IsRefusedAndLeftInPlace()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        TenantTaxProfile earlier = BuildProfile(tenantId, new DateOnly(2026, 1, 1));
        TenantTaxProfile latest = BuildProfile(tenantId, new DateOnly(2026, 6, 1));
        Setup(earlier, latest);

        DeleteTenantTaxProfileCommand command = new DeleteTenantTaxProfileCommand
        {
            TenantId = tenantId,
            Id = earlier.Id,
        };

        // Act
        DeleteTenantTaxProfileResponse response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        response.NotLatest.Should().BeTrue();

        _taxProfileRepositoryMock.Verify(r => r.Remove(It.IsAny<TenantTaxProfile>()), Times.Never);
        _taxProfileRepositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ProfileOfAnotherTenant_IsNotFound()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid foreignProfileId = Guid.NewGuid();

        // The repository filters by tenant, so a foreign id comes back as null even
        // though the row exists — the caller must not learn that it does.
        Setup(null, null);

        DeleteTenantTaxProfileCommand command = new DeleteTenantTaxProfileCommand
        {
            TenantId = tenantId,
            Id = foreignProfileId,
        };

        // Act
        DeleteTenantTaxProfileResponse response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeFalse();
        _taxProfileRepositoryMock.Verify(r => r.Remove(It.IsAny<TenantTaxProfile>()), Times.Never);
        _taxProfileRepositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Lookup_IsScopedToTheRequestedTenant()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        TenantTaxProfile latest = BuildProfile(tenantId, new DateOnly(2026, 6, 1));
        Setup(latest, latest);

        // Act
        await _handler.Handle(
            new DeleteTenantTaxProfileCommand { TenantId = tenantId, Id = latest.Id },
            CancellationToken.None);

        // Assert
        _taxProfileRepositoryMock.Verify(
            r => r.FindByIdAsync(tenantId, latest.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_SingleExistingProfile_CanBeDeleted()
    {
        // Arrange
        // Deleting the only profile is allowed and returns the tenant to the legitimate
        // "tax not configured" state.
        Guid tenantId = Guid.NewGuid();
        TenantTaxProfile only = BuildProfile(tenantId, new DateOnly(2026, 1, 1));
        Setup(only, only);

        // Act
        DeleteTenantTaxProfileResponse response = await _handler.Handle(
            new DeleteTenantTaxProfileCommand { TenantId = tenantId, Id = only.Id },
            CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        response.NotLatest.Should().BeFalse();
        _taxProfileRepositoryMock.Verify(r => r.Remove(only), Times.Once);
    }

    [Fact]
    public void Command_DeclaresAdministratorAsTheOnlyAllowedRole()
    {
        // Arrange
        DeleteTenantTaxProfileCommand command = new DeleteTenantTaxProfileCommand
        {
            TenantId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
        };

        // Act & Assert
        command.AllowedRoles.Should().ContainSingle()
            .Which.Should().Be(Role.Administrator);
    }
}
