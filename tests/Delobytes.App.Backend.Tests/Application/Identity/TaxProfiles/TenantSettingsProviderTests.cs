using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Infrastructure.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity.TaxProfiles;

/// <summary>
/// Tests for TenantSettingsProvider, the seam other modules use to read a tenant's
/// currency, time zone and tax profile.
///
/// The date handed to the repository must be the tenant's local date. Anything else
/// shifts period boundaries by up to a day for every tenant east or west of Greenwich.
/// </summary>
public class TenantSettingsProviderTests
{
    private readonly Mock<ITenantRepository> _tenantRepositoryMock;
    private readonly Mock<ITenantTaxProfileRepository> _taxProfileRepositoryMock;
    private readonly TenantSettingsProvider _provider;

    public TenantSettingsProviderTests()
    {
        _tenantRepositoryMock = new Mock<ITenantRepository>();
        _taxProfileRepositoryMock = new Mock<ITenantTaxProfileRepository>();

        _provider = new TenantSettingsProvider(
            _tenantRepositoryMock.Object,
            _taxProfileRepositoryMock.Object);
    }

    private static Tenant BuildTenant(Guid tenantId, string timeZone = "Europe/Moscow") => new Tenant
    {
        Id = tenantId,
        Name = "Tenant",
        CreatedAt = DateTimeOffset.UtcNow,
        IsActive = true,
        Currency = "RUB",
        TimeZone = timeZone,
    };

    private void SetupTenant(Tenant tenant)
    {
        _tenantRepositoryMock
            .Setup(r => r.FindByIdAsync(tenant.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
    }

    // ── GetAccountingSettingsAsync ────────────────────────────────────────────

    [Fact]
    public async Task GetAccountingSettings_ReturnsCurrencyAndTimeZoneOfTheTenant()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        SetupTenant(BuildTenant(tenantId, "Asia/Vladivostok"));

        // Act
        TenantAccountingSettings settings =
            await _provider.GetAccountingSettingsAsync(tenantId, CancellationToken.None);

        // Assert
        settings.Currency.Should().Be("RUB");
        settings.TimeZoneId.Should().Be("Asia/Vladivostok");
    }

    [Fact]
    public async Task GetAccountingSettings_TenantNotFound_Throws()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();

        _tenantRepositoryMock
            .Setup(r => r.FindByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        // Act
        Func<Task> act = () => _provider.GetAccountingSettingsAsync(tenantId, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{tenantId}*");
    }

    // ── GetTaxProfileAtAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetTaxProfileAt_DecemberEveningUtc_UsesTheNextYearsLocalDate()
    {
        // Arrange
        // 31 December 2025 21:30 UTC is already 1 January 2026 in Moscow, so the new
        // year's profile is the one that applies.
        Guid tenantId = Guid.NewGuid();
        SetupTenant(BuildTenant(tenantId, "Europe/Moscow"));

        _taxProfileRepositoryMock
            .Setup(r => r.GetAtDateAsync(tenantId, new DateOnly(2026, 1, 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantTaxProfile
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Regime = TaxRegime.UsnIncome,
                RatePercent = 6m,
                Vat = VatType.None,
                ValidFrom = new DateOnly(2026, 1, 1),
                CreatedAt = DateTimeOffset.UtcNow,
            });

        DateTimeOffset moment = new DateTimeOffset(2025, 12, 31, 21, 30, 0, TimeSpan.Zero);

        // Act
        TenantTaxProfileSnapshot? snapshot =
            await _provider.GetTaxProfileAtAsync(tenantId, moment, CancellationToken.None);

        // Assert
        _taxProfileRepositoryMock.Verify(
            r => r.GetAtDateAsync(tenantId, new DateOnly(2026, 1, 1), It.IsAny<CancellationToken>()),
            Times.Once);

        snapshot.Should().NotBeNull();
        snapshot!.ValidFrom.Should().Be(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public async Task GetTaxProfileAt_NoProfileOnThatDate_ReturnsNull()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        SetupTenant(BuildTenant(tenantId));

        _taxProfileRepositoryMock
            .Setup(r => r.GetAtDateAsync(tenantId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantTaxProfile?)null);

        // Act
        TenantTaxProfileSnapshot? snapshot = await _provider.GetTaxProfileAtAsync(
            tenantId,
            new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        // Assert
        snapshot.Should().BeNull();
    }

    [Fact]
    public async Task GetTaxProfileAt_ExistingProfile_ReturnsAllSnapshotFields()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        SetupTenant(BuildTenant(tenantId));

        _taxProfileRepositoryMock
            .Setup(r => r.GetAtDateAsync(tenantId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantTaxProfile
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Regime = TaxRegime.UsnIncome,
                RatePercent = 100m,
                Vat = VatType.TwentyTwo,
                ValidFrom = new DateOnly(2026, 1, 1),
                CreatedAt = DateTimeOffset.UtcNow,
            });

        // Act
        TenantTaxProfileSnapshot? snapshot = await _provider.GetTaxProfileAtAsync(
            tenantId,
            new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        // Assert
        snapshot.Should().NotBeNull();
        snapshot!.Regime.Should().Be(TaxRegime.UsnIncome);
        snapshot.RatePercent.Should().Be(100m);
        snapshot.Vat.Should().Be(VatType.TwentyTwo);
        snapshot.ValidFrom.Should().Be(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public async Task GetTaxProfileAt_TenantNotFound_Throws()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();

        _tenantRepositoryMock
            .Setup(r => r.FindByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        // Act
        Func<Task> act = () => _provider.GetTaxProfileAtAsync(
            tenantId,
            new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{tenantId}*");

        _taxProfileRepositoryMock.Verify(
            r => r.GetAtDateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetTaxProfileAt_UnknownTimeZone_ThrowsInsteadOfSilentlyUsingUtc()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        SetupTenant(BuildTenant(tenantId, "Not/AZone"));

        // Act
        Func<Task> act = () => _provider.GetTaxProfileAtAsync(
            tenantId,
            new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Not/AZone*")
            .Where(e => e.InnerException is TimeZoneNotFoundException);

        _taxProfileRepositoryMock.Verify(
            r => r.GetAtDateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("UTC", 2025, 12, 31, 21, 30, 2025, 12, 31)]
    [InlineData("Europe/Moscow", 2025, 12, 31, 21, 30, 2026, 1, 1)]
    [InlineData("Asia/Vladivostok", 2025, 12, 31, 15, 0, 2026, 1, 1)]
    [InlineData("America/New_York", 2026, 1, 1, 3, 0, 2025, 12, 31)]
    public async Task GetTaxProfileAt_EachTimeZone_ResolvesItsOwnCalendarDate(
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
        SetupTenant(BuildTenant(tenantId, timeZoneId));

        DateOnly expectedDate = new DateOnly(expectedYear, expectedMonth, expectedDay);

        _taxProfileRepositoryMock
            .Setup(r => r.GetAtDateAsync(tenantId, expectedDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantTaxProfile?)null);

        // Act
        await _provider.GetTaxProfileAtAsync(
            tenantId,
            new DateTimeOffset(utcYear, utcMonth, utcDay, utcHour, utcMinute, 0, TimeSpan.Zero),
            CancellationToken.None);

        // Assert
        _taxProfileRepositoryMock.Verify(
            r => r.GetAtDateAsync(tenantId, expectedDate, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
