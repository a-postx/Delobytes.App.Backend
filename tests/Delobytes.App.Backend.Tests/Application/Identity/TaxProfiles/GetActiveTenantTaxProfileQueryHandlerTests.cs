using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Application.Queries.TaxProfiles.GetActiveTenantTaxProfile;
using Delobytes.App.Backend.Identity.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity.TaxProfiles;

/// <summary>
/// Tests for GetActiveTenantTaxProfileQueryHandler.
///
/// The profile is selected by the tenant's LOCAL calendar date, not UTC: an order placed
/// at 00:30 Moscow time on 1 January arrives as 21:30 UTC on 31 December and must pick up
/// the new year's profile.
/// </summary>
public class GetActiveTenantTaxProfileQueryHandlerTests
{
    private readonly Mock<ITenantTaxProfileRepository> _taxProfileRepositoryMock;
    private readonly Mock<ITenantRepository> _tenantRepositoryMock;
    private readonly GetActiveTenantTaxProfileQueryHandler _handler;

    public GetActiveTenantTaxProfileQueryHandlerTests()
    {
        _taxProfileRepositoryMock = new Mock<ITenantTaxProfileRepository>();
        _tenantRepositoryMock = new Mock<ITenantRepository>();

        _handler = new GetActiveTenantTaxProfileQueryHandler(
            _taxProfileRepositoryMock.Object,
            _tenantRepositoryMock.Object);
    }

    private static Tenant BuildTenant(Guid tenantId, string timeZone = "Europe/Moscow") => new Tenant
    {
        Id = tenantId,
        Name = "Tenant",
        CreatedAt = DateTimeOffset.UtcNow,
        IsActive = true,
        TimeZone = timeZone,
    };

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

    private void SetupTenant(Tenant tenant)
    {
        _tenantRepositoryMock
            .Setup(r => r.FindByIdAsync(tenant.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
    }

    [Fact]
    public async Task Handle_MomentInTenantTimeZone_PicksTheProfileOfTheLocalDate()
    {
        // Arrange
        // This is the case the whole time-zone conversion exists for: the UTC calendar
        // date is still 31 December, while the tenant has already entered 1 January.
        Guid tenantId = Guid.NewGuid();
        Tenant tenant = BuildTenant(tenantId, "Europe/Moscow");
        SetupTenant(tenant);

        TenantTaxProfile newYearProfile = BuildProfile(tenantId, new DateOnly(2026, 1, 1));

        _taxProfileRepositoryMock
            .Setup(r => r.GetAtDateAsync(tenantId, new DateOnly(2026, 1, 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newYearProfile);

        // Wednesday, 31 December 2025 21:30 UTC == 1 January 2026 00:30 Moscow time.
        DateTimeOffset moment = new DateTimeOffset(2025, 12, 31, 21, 30, 0, TimeSpan.Zero);

        GetActiveTenantTaxProfileQuery query = new GetActiveTenantTaxProfileQuery
        {
            TenantId = tenantId,
            At = moment,
        };

        // Act
        GetActiveTenantTaxProfileResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        _taxProfileRepositoryMock.Verify(
            r => r.GetAtDateAsync(tenantId, new DateOnly(2026, 1, 1), It.IsAny<CancellationToken>()),
            Times.Once);

        response.Found.Should().BeTrue();
        response.Id.Should().Be(newYearProfile.Id);
        response.ValidFrom.Should().Be(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public async Task Handle_MomentInTenantTimeZone_DoesNotFallBackToTheUtcDate()
    {
        // Arrange
        // Negative counterpart of the test above: the 31 December profile exists and would
        // be returned if the handler wrongly used the UTC date.
        Guid tenantId = Guid.NewGuid();
        Tenant tenant = BuildTenant(tenantId, "Europe/Moscow");
        SetupTenant(tenant);

        TenantTaxProfile oldYearProfile = BuildProfile(tenantId, new DateOnly(2025, 1, 1));

        _taxProfileRepositoryMock
            .Setup(r => r.GetAtDateAsync(tenantId, new DateOnly(2025, 12, 31), It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldYearProfile);

        _taxProfileRepositoryMock
            .Setup(r => r.GetAtDateAsync(tenantId, new DateOnly(2026, 1, 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantTaxProfile?)null);

        DateTimeOffset moment = new DateTimeOffset(2025, 12, 31, 21, 30, 0, TimeSpan.Zero);

        GetActiveTenantTaxProfileQuery query = new GetActiveTenantTaxProfileQuery
        {
            TenantId = tenantId,
            At = moment,
        };

        // Act
        GetActiveTenantTaxProfileResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        _taxProfileRepositoryMock.Verify(
            r => r.GetAtDateAsync(tenantId, new DateOnly(2025, 12, 31), It.IsAny<CancellationToken>()),
            Times.Never);

        response.Found.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NoProfileOnThatDate_ReportsFoundFalseInsteadOfThrowing()
    {
        // Arrange
        // A new tenant simply has no tax profile yet. That is a normal state, so it must
        // surface as found = false and never as an exception or a 404.
        Guid tenantId = Guid.NewGuid();
        Tenant tenant = BuildTenant(tenantId);
        SetupTenant(tenant);

        _taxProfileRepositoryMock
            .Setup(r => r.GetAtDateAsync(tenantId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantTaxProfile?)null);

        GetActiveTenantTaxProfileQuery query = new GetActiveTenantTaxProfileQuery
        {
            TenantId = tenantId,
            At = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
        };

        // Act
        GetActiveTenantTaxProfileResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.Found.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ExistingProfile_ReturnsAllSnapshotFields()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Tenant tenant = BuildTenant(tenantId);
        SetupTenant(tenant);

        TenantTaxProfile profile = new TenantTaxProfile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Regime = TaxRegime.UsnIncome,
            RatePercent = 6m,
            Vat = VatType.Five,
            ValidFrom = new DateOnly(2026, 1, 1),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _taxProfileRepositoryMock
            .Setup(r => r.GetAtDateAsync(tenantId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        GetActiveTenantTaxProfileQuery query = new GetActiveTenantTaxProfileQuery
        {
            TenantId = tenantId,
            At = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
        };

        // Act
        GetActiveTenantTaxProfileResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        response.Id.Should().Be(profile.Id);
        response.Regime.Should().Be(TaxRegime.UsnIncome);
        response.RatePercent.Should().Be(6m);
        response.Vat.Should().Be(VatType.Five);
        response.ValidFrom.Should().Be(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public async Task Handle_TenantNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();

        _tenantRepositoryMock
            .Setup(r => r.FindByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        GetActiveTenantTaxProfileQuery query = new GetActiveTenantTaxProfileQuery
        {
            TenantId = tenantId,
            At = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.Handle(query, CancellationToken.None));

        _taxProfileRepositoryMock.Verify(
            r => r.GetAtDateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownTimeZone_ThrowsInsteadOfSilentlyUsingUtc()
    {
        // Arrange
        // Falling back to UTC would shift period boundaries by up to a day, so an
        // unresolvable zone must fail loudly rather than produce a plausible wrong answer.
        Guid tenantId = Guid.NewGuid();
        Tenant tenant = BuildTenant(tenantId, "Not/AZone");
        SetupTenant(tenant);

        GetActiveTenantTaxProfileQuery query = new GetActiveTenantTaxProfileQuery
        {
            TenantId = tenantId,
            At = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
        };

        // Act & Assert
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.Handle(query, CancellationToken.None));

        exception.Message.Should().Contain("Not/AZone");
        exception.InnerException.Should().BeOfType<TimeZoneNotFoundException>();

        _taxProfileRepositoryMock.Verify(
            r => r.GetAtDateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("UTC", 2025, 12, 31, 21, 30, 2025, 12, 31)]
    [InlineData("Europe/Moscow", 2025, 12, 31, 21, 30, 2026, 1, 1)]
    [InlineData("Asia/Vladivostok", 2025, 12, 31, 15, 0, 2026, 1, 1)]
    public async Task Handle_EachTimeZone_ResolvesItsOwnCalendarDate(
        string timeZoneId,
        int utcYear,
        int utcMonth,
        int utcDay,
        int utcHour,
        int utcMinute,
        int expectedYear,
        int expectedMonth,
        int expectedDay)
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Tenant tenant = BuildTenant(tenantId, timeZoneId);
        SetupTenant(tenant);

        DateOnly expectedDate = new DateOnly(expectedYear, expectedMonth, expectedDay);

        _taxProfileRepositoryMock
            .Setup(r => r.GetAtDateAsync(tenantId, expectedDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildProfile(tenantId, expectedDate));

        GetActiveTenantTaxProfileQuery query = new GetActiveTenantTaxProfileQuery
        {
            TenantId = tenantId,
            At = new DateTimeOffset(utcYear, utcMonth, utcDay, utcHour, utcMinute, 0, TimeSpan.Zero),
        };

        // Act
        await _handler.Handle(query, CancellationToken.None);

        // Assert
        _taxProfileRepositoryMock.Verify(
            r => r.GetAtDateAsync(tenantId, expectedDate, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_AtNotSupplied_UsesTheCurrentMoment()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Tenant tenant = BuildTenant(tenantId, "UTC");
        SetupTenant(tenant);

        _taxProfileRepositoryMock
            .Setup(r => r.GetAtDateAsync(tenantId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantTaxProfile?)null);

        GetActiveTenantTaxProfileQuery query = new GetActiveTenantTaxProfileQuery
        {
            TenantId = tenantId,
            At = null,
        };

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Act
        await _handler.Handle(query, CancellationToken.None);

        // Assert
        _taxProfileRepositoryMock.Verify(
            r => r.GetAtDateAsync(tenantId, It.Is<DateOnly>(d => d == today || d == today.AddDays(-1) || d == today.AddDays(1)), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
