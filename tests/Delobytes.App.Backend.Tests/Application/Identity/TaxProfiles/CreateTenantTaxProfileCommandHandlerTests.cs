using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Identity.Application.Commands.TaxProfiles.CreateTenantTaxProfile;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity.TaxProfiles;

/// <summary>
/// Tests for CreateTenantTaxProfileCommandHandler.
///
/// Profiles are immutable: a change is a new row with a later ValidFrom, never an update
/// of an existing one, otherwise closed periods would change retroactively.
/// </summary>
public class CreateTenantTaxProfileCommandHandlerTests
{
    private readonly Mock<ITenantTaxProfileRepository> _taxProfileRepositoryMock;
    private readonly Mock<IUserContext> _userContextMock;
    private readonly CreateTenantTaxProfileCommandHandler _handler;

    public CreateTenantTaxProfileCommandHandlerTests()
    {
        _taxProfileRepositoryMock = new Mock<ITenantTaxProfileRepository>();
        _userContextMock = new Mock<IUserContext>();

        _handler = new CreateTenantTaxProfileCommandHandler(
            _taxProfileRepositoryMock.Object,
            _userContextMock.Object);
    }

    private static TenantTaxProfile BuildProfile(
        Guid tenantId,
        DateOnly validFrom,
        decimal ratePercent = 6m) => new TenantTaxProfile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Regime = TaxRegime.UsnIncome,
            RatePercent = ratePercent,
            Vat = VatType.None,
            ValidFrom = validFrom,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static CreateTenantTaxProfileCommand BuildCommand(Guid tenantId, DateOnly validFrom) =>
        new CreateTenantTaxProfileCommand
        {
            TenantId = tenantId,
            Regime = TaxRegime.UsnIncome,
            RatePercent = 6m,
            Vat = VatType.None,
            ValidFrom = validFrom,
        };

    private void SetupLatest(TenantTaxProfile? latest)
    {
        _taxProfileRepositoryMock
            .Setup(r => r.GetLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(latest);

        _taxProfileRepositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    [Fact]
    public async Task Handle_NoExistingProfile_CreatesTheFirstVersion()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        DateOnly validFrom = new DateOnly(2026, 1, 1);
        SetupLatest(null);

        TenantTaxProfile? added = null;

        _taxProfileRepositoryMock
            .Setup(r => r.Add(It.IsAny<TenantTaxProfile>()))
            .Callback<TenantTaxProfile>(profile => added = profile);

        // Act
        CreateTenantTaxProfileResponse response =
            await _handler.Handle(BuildCommand(tenantId, validFrom), CancellationToken.None);

        // Assert
        response.Conflict.Should().BeFalse();
        added.Should().NotBeNull();
        added!.TenantId.Should().Be(tenantId);
        added.ValidFrom.Should().Be(validFrom);
        added.Regime.Should().Be(TaxRegime.UsnIncome);

        _taxProfileRepositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidFromStrictlyAfterLatest_IsAccepted()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        TenantTaxProfile latest = BuildProfile(tenantId, new DateOnly(2026, 1, 1));
        SetupLatest(latest);

        CreateTenantTaxProfileCommand command = BuildCommand(tenantId, new DateOnly(2026, 6, 1));

        // Act
        CreateTenantTaxProfileResponse response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        response.Conflict.Should().BeFalse();
        _taxProfileRepositoryMock.Verify(r => r.Add(It.IsAny<TenantTaxProfile>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidFromEqualToLatest_ReportsConflictAndWritesNothing()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        DateOnly validFrom = new DateOnly(2026, 1, 1);
        TenantTaxProfile latest = BuildProfile(tenantId, validFrom);
        SetupLatest(latest);

        // Act
        CreateTenantTaxProfileResponse response =
            await _handler.Handle(BuildCommand(tenantId, validFrom), CancellationToken.None);

        // Assert
        response.Conflict.Should().BeTrue();
        _taxProfileRepositoryMock.Verify(r => r.Add(It.IsAny<TenantTaxProfile>()), Times.Never);
        _taxProfileRepositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ValidFromEarlierThanLatest_ReportsConflictAndWritesNothing()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        TenantTaxProfile latest = BuildProfile(tenantId, new DateOnly(2026, 6, 1));
        SetupLatest(latest);

        CreateTenantTaxProfileCommand command = BuildCommand(tenantId, new DateOnly(2026, 1, 1));

        // Act
        CreateTenantTaxProfileResponse response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        // A profile effective in the past would rewrite a period that has already been
        // calculated, which is never allowed.
        response.Conflict.Should().BeTrue();
        _taxProfileRepositoryMock.Verify(r => r.Add(It.IsAny<TenantTaxProfile>()), Times.Never);
    }

    [Fact]
    public async Task Handle_StampsCreatedAtAndCreatedByUserId()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        SetupLatest(null);

        _userContextMock.Setup(c => c.UserId).Returns(userId);

        TenantTaxProfile? added = null;

        _taxProfileRepositoryMock
            .Setup(r => r.Add(It.IsAny<TenantTaxProfile>()))
            .Callback<TenantTaxProfile>(profile => added = profile);

        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Act
        await _handler.Handle(BuildCommand(tenantId, new DateOnly(2026, 1, 1)), CancellationToken.None);

        // Assert
        added.Should().NotBeNull();
        added!.CreatedByUserId.Should().Be(userId);
        added.CreatedAt.Should().BeOnOrAfter(before);
        added.CreatedAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Handle_WithoutAuthenticatedUser_LeavesCreatedByUserIdNull()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        SetupLatest(null);

        _userContextMock.Setup(c => c.UserId).Returns((Guid?)null);

        TenantTaxProfile? added = null;

        _taxProfileRepositoryMock
            .Setup(r => r.Add(It.IsAny<TenantTaxProfile>()))
            .Callback<TenantTaxProfile>(profile => added = profile);

        // Act
        await _handler.Handle(BuildCommand(tenantId, new DateOnly(2026, 1, 1)), CancellationToken.None);

        // Assert
        added.Should().NotBeNull();
        added!.CreatedByUserId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_RateOfOneHundredPercent_IsPersistedAsIs()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        SetupLatest(null);

        TenantTaxProfile? added = null;

        _taxProfileRepositoryMock
            .Setup(r => r.Add(It.IsAny<TenantTaxProfile>()))
            .Callback<TenantTaxProfile>(profile => added = profile);

        CreateTenantTaxProfileCommand command = BuildCommand(tenantId, new DateOnly(2026, 1, 1));
        command.RatePercent = 100m;

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        added.Should().NotBeNull();
        added!.RatePercent.Should().Be(100m);
    }

    [Fact]
    public void Command_DeclaresAdministratorAsTheOnlyAllowedRole()
    {
        // Arrange
        CreateTenantTaxProfileCommand command = BuildCommand(Guid.NewGuid(), new DateOnly(2026, 1, 1));

        // Act & Assert
        command.AllowedRoles.Should().ContainSingle()
            .Which.Should().Be(Role.Administrator);
    }
}
