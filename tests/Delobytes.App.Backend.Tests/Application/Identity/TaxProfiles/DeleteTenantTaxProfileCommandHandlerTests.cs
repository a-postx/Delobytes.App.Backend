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
/// Two independent rules protect historical data:
/// 1. Only the latest version may be removed (no gaps in history).
/// 2. The latest version may only be removed while it has not taken effect yet
///    (ValidFrom is strictly in the future, in the tenant's own time zone). Once it is
///    effective, it may already have been used in reports; deleting it would silently
///    change numbers that were already reported on.
/// </summary>
public class DeleteTenantTaxProfileCommandHandlerTests
{
    private readonly Mock<ITenantTaxProfileRepository> _taxProfileRepositoryMock;
    private readonly Mock<ITenantRepository> _tenantRepositoryMock;
    private readonly DeleteTenantTaxProfileCommandHandler _handler;

    public DeleteTenantTaxProfileCommandHandlerTests()
    {
        _taxProfileRepositoryMock = new Mock<ITenantTaxProfileRepository>();
        _tenantRepositoryMock = new Mock<ITenantRepository>();
        _handler = new DeleteTenantTaxProfileCommandHandler(
            _taxProfileRepositoryMock.Object,
            _tenantRepositoryMock.Object);
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

    private static Tenant BuildTenant(Guid tenantId, string timeZone = "Europe/Moscow") => new Tenant
    {
        Id = tenantId,
        Name = "Тест",
        TimeZone = timeZone,
        CreatedAt = DateTimeOffset.UtcNow,
        IsActive = true,
    };

    private void Setup(TenantTaxProfile? found, TenantTaxProfile? latest, Tenant? tenant = null)
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

        _tenantRepositoryMock
            .Setup(r => r.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
    }

    /// <summary>Завтра в часовом поясе Europe/Moscow, безопасно далеко от момента запуска тестов.</summary>
    private static DateOnly MoscowTomorrow()
    {
        TimeZoneInfo moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, moscow).DateTime);
        return today.AddDays(1);
    }

    /// <summary>Сегодня в часовом поясе Europe/Moscow.</summary>
    private static DateOnly MoscowToday()
    {
        TimeZoneInfo moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, moscow).DateTime);
    }

    [Fact]
    public async Task Handle_LatestProfileInTheFuture_IsRemoved()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        TenantTaxProfile latest = BuildProfile(tenantId, MoscowTomorrow());
        Setup(latest, latest, BuildTenant(tenantId));

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
        response.AlreadyEffective.Should().BeFalse();

        _taxProfileRepositoryMock.Verify(r => r.Remove(latest), Times.Once);
        _taxProfileRepositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_EarlierProfile_IsRefusedAndLeftInPlace()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        TenantTaxProfile earlier = BuildProfile(tenantId, new DateOnly(2026, 1, 1));
        TenantTaxProfile latest = BuildProfile(tenantId, MoscowTomorrow());
        Setup(earlier, latest, BuildTenant(tenantId));

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
        response.AlreadyEffective.Should().BeFalse();

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
        TenantTaxProfile latest = BuildProfile(tenantId, MoscowTomorrow());
        Setup(latest, latest, BuildTenant(tenantId));

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
    public async Task Handle_SingleProfileInTheFuture_CanBeDeleted()
    {
        // Arrange
        // A single not-yet-effective profile is just a draft: deleting it returns the
        // tenant to the legitimate "tax not configured" state without touching history.
        Guid tenantId = Guid.NewGuid();
        TenantTaxProfile only = BuildProfile(tenantId, MoscowTomorrow());
        Setup(only, only, BuildTenant(tenantId));

        // Act
        DeleteTenantTaxProfileResponse response = await _handler.Handle(
            new DeleteTenantTaxProfileCommand { TenantId = tenantId, Id = only.Id },
            CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        response.NotLatest.Should().BeFalse();
        response.AlreadyEffective.Should().BeFalse();
        _taxProfileRepositoryMock.Verify(r => r.Remove(only), Times.Once);
    }

    [Fact]
    public async Task Handle_SingleProfileEffectiveToday_IsRefusedAndLeftInPlace()
    {
        // Arrange
        // This is exactly the bug being fixed: being the only version does not make a
        // profile safe to delete once its ValidFrom has arrived. A tenant with exactly
        // one profile effective today must not be able to erase its tax history.
        Guid tenantId = Guid.NewGuid();
        TenantTaxProfile only = BuildProfile(tenantId, MoscowToday());
        Setup(only, only, BuildTenant(tenantId));

        // Act
        DeleteTenantTaxProfileResponse response = await _handler.Handle(
            new DeleteTenantTaxProfileCommand { TenantId = tenantId, Id = only.Id },
            CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        response.NotLatest.Should().BeFalse();
        response.AlreadyEffective.Should().BeTrue();
        _taxProfileRepositoryMock.Verify(r => r.Remove(It.IsAny<TenantTaxProfile>()), Times.Never);
        _taxProfileRepositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SingleProfileEffectiveInThePast_IsRefusedAndLeftInPlace()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        TenantTaxProfile only = BuildProfile(tenantId, new DateOnly(2020, 1, 1));
        Setup(only, only, BuildTenant(tenantId));

        // Act
        DeleteTenantTaxProfileResponse response = await _handler.Handle(
            new DeleteTenantTaxProfileCommand { TenantId = tenantId, Id = only.Id },
            CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        response.AlreadyEffective.Should().BeTrue();
        _taxProfileRepositoryMock.Verify(r => r.Remove(It.IsAny<TenantTaxProfile>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SecondProfileEffectiveToday_IsRefusedAndLeftInPlace()
    {
        // Arrange
        // After adding a second profile the first becomes undeletable (not latest).
        // The second must also be undeletable once its own ValidFrom has arrived —
        // otherwise the tenant ends up with no profile at all for an already-reported
        // period, which is the scenario raised by the user.
        Guid tenantId = Guid.NewGuid();
        TenantTaxProfile first = BuildProfile(tenantId, new DateOnly(2025, 1, 1));
        TenantTaxProfile second = BuildProfile(tenantId, MoscowToday());
        Setup(second, second, BuildTenant(tenantId));

        // Act
        DeleteTenantTaxProfileResponse response = await _handler.Handle(
            new DeleteTenantTaxProfileCommand { TenantId = tenantId, Id = second.Id },
            CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        response.NotLatest.Should().BeFalse();
        response.AlreadyEffective.Should().BeTrue();
        _taxProfileRepositoryMock.Verify(r => r.Remove(It.IsAny<TenantTaxProfile>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SecondProfileScheduledInTheFuture_CanBeDeleted()
    {
        // Arrange
        // A second profile whose ValidFrom has not arrived yet is still a draft and can
        // be cancelled, even though a first (now permanent) profile already exists.
        Guid tenantId = Guid.NewGuid();
        TenantTaxProfile first = BuildProfile(tenantId, new DateOnly(2025, 1, 1));
        TenantTaxProfile second = BuildProfile(tenantId, MoscowTomorrow());
        Setup(second, second, BuildTenant(tenantId));

        // Act
        DeleteTenantTaxProfileResponse response = await _handler.Handle(
            new DeleteTenantTaxProfileCommand { TenantId = tenantId, Id = second.Id },
            CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        response.NotLatest.Should().BeFalse();
        response.AlreadyEffective.Should().BeFalse();
        _taxProfileRepositoryMock.Verify(r => r.Remove(second), Times.Once);
    }

    [Fact]
    public async Task Handle_NotLatestCheck_TakesPriorityOverEffectiveDateCheck()
    {
        // Arrange
        // If a profile is both not the latest AND already effective, the response must
        // report NotLatest: that is the more specific and more actionable reason, and
        // the handler must not evaluate dates for a version it already refuses to touch.
        Guid tenantId = Guid.NewGuid();
        TenantTaxProfile earlier = BuildProfile(tenantId, new DateOnly(2020, 1, 1));
        TenantTaxProfile latest = BuildProfile(tenantId, MoscowTomorrow());
        Setup(earlier, latest, BuildTenant(tenantId));

        // Act
        DeleteTenantTaxProfileResponse response = await _handler.Handle(
            new DeleteTenantTaxProfileCommand { TenantId = tenantId, Id = earlier.Id },
            CancellationToken.None);

        // Assert
        response.NotLatest.Should().BeTrue();
        response.AlreadyEffective.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_UsesTenantTimeZone_NotUtc()
    {
        // Arrange
        // A profile whose ValidFrom is "today" in UTC may already be a day in the past
        // in the tenant's own time zone (e.g. far-east zones), or still be in the future
        // for zones behind UTC. The handler must resolve "today" via the tenant's own
        // time zone, exactly like GetActiveTenantTaxProfileQueryHandler does.
        Guid tenantId = Guid.NewGuid();

        // Kamchatka is UTC+12: "tomorrow" there can already have started while it is
        // still "today" in UTC, so a profile dated for Kamchatka's tomorrow must still
        // be deletable from the point of view of UTC "today".
        TimeZoneInfo kamchatka = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kamchatka");
        DateOnly kamchatkaToday = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, kamchatka).DateTime);
        DateOnly kamchatkaTomorrow = kamchatkaToday.AddDays(1);

        TenantTaxProfile future = BuildProfile(tenantId, kamchatkaTomorrow);
        Setup(future, future, BuildTenant(tenantId, "Asia/Kamchatka"));

        // Act
        DeleteTenantTaxProfileResponse response = await _handler.Handle(
            new DeleteTenantTaxProfileCommand { TenantId = tenantId, Id = future.Id },
            CancellationToken.None);

        // Assert
        response.AlreadyEffective.Should().BeFalse();
        _taxProfileRepositoryMock.Verify(r => r.Remove(future), Times.Once);
    }

    [Fact]
    public async Task Handle_UnknownTenantTimeZone_Throws()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        TenantTaxProfile latest = BuildProfile(tenantId, MoscowTomorrow());
        Setup(latest, latest, BuildTenant(tenantId, "Not/A_Real_Zone"));

        // Act
        Func<Task> act = async () => await _handler.Handle(
            new DeleteTenantTaxProfileCommand { TenantId = tenantId, Id = latest.Id },
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Handle_TenantNotFound_Throws()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        TenantTaxProfile latest = BuildProfile(tenantId, MoscowTomorrow());
        Setup(latest, latest, tenant: null);

        // Act
        Func<Task> act = async () => await _handler.Handle(
            new DeleteTenantTaxProfileCommand { TenantId = tenantId, Id = latest.Id },
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
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
