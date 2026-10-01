using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Infrastructure.Persistence;
using Delobytes.App.Backend.Identity.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity.TaxProfiles;

/// <summary>
/// Integration tests for TenantTaxProfileRepository against the in-memory provider.
///
/// Two things this file deliberately does NOT claim. The in-memory provider ignores
/// HasPrecision, so the numeric(5,2) capacity is verified by the migration, not here. It
/// also does not enforce unique indexes, so the (TenantId, ValidFrom) guarantee is
/// exercised by the handler tests and by the migration.
///
/// Cross-tenant cases need two contexts over one database: IdentityDbContext.SaveChanges
/// overwrites TenantId on every added ITenantScoped entity with the ambient tenant, so a
/// foreign-tenant row cannot be seeded from a context that is already scoped elsewhere.
/// </summary>
public class TenantTaxProfileRepositoryTests
{
    private static DbContextOptions<IdentityDbContext> BuildOptions() =>
        new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase("tax-profiles-" + Guid.NewGuid())
            .Options;

    private static IdentityDbContext BuildContext(DbContextOptions<IdentityDbContext> options, Guid? tenantId) =>
        new IdentityDbContext(options, BuildTenantContext(tenantId).Object);

    private static Mock<ITenantContext> BuildTenantContext(Guid? tenantId)
    {
        Mock<ITenantContext> mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        return mock;
    }

    private static TenantTaxProfile BuildProfile(
        Guid tenantId,
        DateOnly validFrom,
        decimal ratePercent = 6m,
        VatType vat = VatType.None) => new TenantTaxProfile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Regime = TaxRegime.UsnIncome,
            RatePercent = ratePercent,
            Vat = vat,
            ValidFrom = validFrom,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task GetByTenantAsync_ReturnsVersionsNewestFirst()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        DbContextOptions<IdentityDbContext> options = BuildOptions();

        using IdentityDbContext context = BuildContext(options, tenantId);
        context.TenantTaxProfiles.AddRange(
            BuildProfile(tenantId, new DateOnly(2025, 1, 1)),
            BuildProfile(tenantId, new DateOnly(2025, 6, 1)),
            BuildProfile(tenantId, new DateOnly(2026, 1, 1)));
        await context.SaveChangesAsync();

        TenantTaxProfileRepository repository = new TenantTaxProfileRepository(context);

        // Act
        IReadOnlyList<TenantTaxProfile> profiles = await repository.GetByTenantAsync(tenantId, CancellationToken.None);

        // Assert
        profiles.Should().HaveCount(3);
        profiles[0].ValidFrom.Should().Be(new DateOnly(2026, 1, 1));
        profiles[1].ValidFrom.Should().Be(new DateOnly(2025, 6, 1));
        profiles[2].ValidFrom.Should().Be(new DateOnly(2025, 1, 1));
    }

    [Fact]
    public async Task GetByTenantAsync_ExcludesProfilesOfOtherTenants()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid otherTenantId = Guid.NewGuid();
        DbContextOptions<IdentityDbContext> options = BuildOptions();

        using (IdentityDbContext seedContext = BuildContext(options, otherTenantId))
        {
            seedContext.TenantTaxProfiles.Add(BuildProfile(otherTenantId, new DateOnly(2026, 1, 1)));
            await seedContext.SaveChangesAsync();
        }

        using IdentityDbContext context = BuildContext(options, tenantId);
        TenantTaxProfile ownProfile = BuildProfile(tenantId, new DateOnly(2026, 1, 1));
        context.TenantTaxProfiles.Add(ownProfile);
        await context.SaveChangesAsync();

        TenantTaxProfileRepository repository = new TenantTaxProfileRepository(context);

        // Act
        IReadOnlyList<TenantTaxProfile> profiles = await repository.GetByTenantAsync(tenantId, CancellationToken.None);

        // Assert
        TenantTaxProfile only = profiles.Should().ContainSingle().Subject;
        only.Id.Should().Be(ownProfile.Id);
    }

    [Fact]
    public async Task GetAtDateAsync_PicksTheClosestVersionNotAfterTheDate()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        DbContextOptions<IdentityDbContext> options = BuildOptions();

        using IdentityDbContext context = BuildContext(options, tenantId);
        context.TenantTaxProfiles.AddRange(
            BuildProfile(tenantId, new DateOnly(2025, 1, 1), 6m),
            BuildProfile(tenantId, new DateOnly(2025, 6, 1), 7m),
            BuildProfile(tenantId, new DateOnly(2026, 1, 1), 8m));
        await context.SaveChangesAsync();

        TenantTaxProfileRepository repository = new TenantTaxProfileRepository(context);

        // Act
        TenantTaxProfile? profile = await repository.GetAtDateAsync(tenantId, new DateOnly(2025, 9, 15), CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile!.ValidFrom.Should().Be(new DateOnly(2025, 6, 1));
        profile.RatePercent.Should().Be(7m);
    }

    [Fact]
    public async Task GetAtDateAsync_DateBeforeEveryVersion_ReturnsNull()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        DbContextOptions<IdentityDbContext> options = BuildOptions();

        using IdentityDbContext context = BuildContext(options, tenantId);
        context.TenantTaxProfiles.Add(BuildProfile(tenantId, new DateOnly(2026, 1, 1)));
        await context.SaveChangesAsync();

        TenantTaxProfileRepository repository = new TenantTaxProfileRepository(context);

        // Act
        TenantTaxProfile? profile = await repository.GetAtDateAsync(tenantId, new DateOnly(2025, 12, 31), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task GetAtDateAsync_ExactlyOnValidFrom_ReturnsThatVersion()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        DbContextOptions<IdentityDbContext> options = BuildOptions();

        using IdentityDbContext context = BuildContext(options, tenantId);
        TenantTaxProfile expected = BuildProfile(tenantId, new DateOnly(2026, 1, 1));
        context.TenantTaxProfiles.Add(expected);
        await context.SaveChangesAsync();

        TenantTaxProfileRepository repository = new TenantTaxProfileRepository(context);

        // Act
        TenantTaxProfile? profile = await repository.GetAtDateAsync(tenantId, new DateOnly(2026, 1, 1), CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile!.Id.Should().Be(expected.Id);
    }

    [Fact]
    public async Task GetAtDateAsync_PicksTheNewerVersionOnceItsDateHasArrived()
    {
        // Arrange
        // The boundary the time-zone conversion feeds into: on 1 January the new profile
        // wins over the previous year's.
        Guid tenantId = Guid.NewGuid();
        DbContextOptions<IdentityDbContext> options = BuildOptions();

        using IdentityDbContext context = BuildContext(options, tenantId);
        context.TenantTaxProfiles.AddRange(
            BuildProfile(tenantId, new DateOnly(2025, 1, 1), 6m),
            BuildProfile(tenantId, new DateOnly(2026, 1, 1), 7m));
        await context.SaveChangesAsync();

        TenantTaxProfileRepository repository = new TenantTaxProfileRepository(context);

        // Act
        TenantTaxProfile? onNewYear = await repository.GetAtDateAsync(tenantId, new DateOnly(2026, 1, 1), CancellationToken.None);
        TenantTaxProfile? dayBefore = await repository.GetAtDateAsync(tenantId, new DateOnly(2025, 12, 31), CancellationToken.None);

        // Assert
        onNewYear!.RatePercent.Should().Be(7m);
        dayBefore!.RatePercent.Should().Be(6m);
    }

    [Fact]
    public async Task GetLatestAsync_ReturnsTheVersionWithTheHighestValidFrom()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        DbContextOptions<IdentityDbContext> options = BuildOptions();

        using IdentityDbContext context = BuildContext(options, tenantId);
        context.TenantTaxProfiles.AddRange(
            BuildProfile(tenantId, new DateOnly(2025, 1, 1)),
            BuildProfile(tenantId, new DateOnly(2026, 1, 1)),
            BuildProfile(tenantId, new DateOnly(2025, 6, 1)));
        await context.SaveChangesAsync();

        TenantTaxProfileRepository repository = new TenantTaxProfileRepository(context);

        // Act
        TenantTaxProfile? latest = await repository.GetLatestAsync(tenantId, CancellationToken.None);

        // Assert
        latest.Should().NotBeNull();
        latest!.ValidFrom.Should().Be(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public async Task GetLatestAsync_NoProfiles_ReturnsNull()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();

        using IdentityDbContext context = BuildContext(BuildOptions(), tenantId);
        TenantTaxProfileRepository repository = new TenantTaxProfileRepository(context);

        // Act
        TenantTaxProfile? latest = await repository.GetLatestAsync(tenantId, CancellationToken.None);

        // Assert
        latest.Should().BeNull();
    }

    [Fact]
    public async Task FindByIdAsync_ProfileOfAnotherTenant_ReturnsNull()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid otherTenantId = Guid.NewGuid();
        DbContextOptions<IdentityDbContext> options = BuildOptions();

        TenantTaxProfile foreign = BuildProfile(otherTenantId, new DateOnly(2026, 1, 1));

        using (IdentityDbContext seedContext = BuildContext(options, otherTenantId))
        {
            seedContext.TenantTaxProfiles.Add(foreign);
            await seedContext.SaveChangesAsync();
        }

        using IdentityDbContext context = BuildContext(options, tenantId);
        TenantTaxProfileRepository repository = new TenantTaxProfileRepository(context);

        // Act
        TenantTaxProfile? profile = await repository.FindByIdAsync(tenantId, foreign.Id, CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task SaveChangesAsync_RateOfExactlyOneHundredPercent_RoundTripsUnchanged()
    {
        // Arrange
        // The retired Tenant.TaxRatePercent column was numeric(8,6) and could only hold up
        // to 99.999999, so saving 100 failed at the database. The replacement column is
        // numeric(5,2) and the validator allows the inclusive 0–100 range. The in-memory
        // provider ignores HasPrecision, so this test covers the decimal round-trip only —
        // the column's capacity itself is asserted by the migration.
        Guid tenantId = Guid.NewGuid();

        using IdentityDbContext context = BuildContext(BuildOptions(), tenantId);
        TenantTaxProfileRepository repository = new TenantTaxProfileRepository(context);

        TenantTaxProfile profile = BuildProfile(tenantId, new DateOnly(2026, 1, 1), 100m);

        // Act
        repository.Add(profile);
        await repository.SaveChangesAsync(CancellationToken.None);

        // Assert
        TenantTaxProfile? reloaded = await repository.FindByIdAsync(tenantId, profile.Id, CancellationToken.None);
        reloaded.Should().NotBeNull();
        reloaded!.RatePercent.Should().Be(100m);
    }

    [Fact]
    public async Task SaveChangesAsync_TwoDecimalPlaces_ArePreserved()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();

        using IdentityDbContext context = BuildContext(BuildOptions(), tenantId);
        TenantTaxProfileRepository repository = new TenantTaxProfileRepository(context);

        TenantTaxProfile profile = BuildProfile(tenantId, new DateOnly(2026, 1, 1), 6.25m);

        // Act
        repository.Add(profile);
        await repository.SaveChangesAsync(CancellationToken.None);

        // Assert
        TenantTaxProfile? reloaded = await repository.FindByIdAsync(tenantId, profile.Id, CancellationToken.None);
        reloaded!.RatePercent.Should().Be(6.25m);
    }

    [Fact]
    public async Task Remove_LatestProfile_DeletesOnlyThatRow()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();

        using IdentityDbContext context = BuildContext(BuildOptions(), tenantId);
        TenantTaxProfile older = BuildProfile(tenantId, new DateOnly(2025, 1, 1));
        TenantTaxProfile latest = BuildProfile(tenantId, new DateOnly(2026, 1, 1));
        context.TenantTaxProfiles.AddRange(older, latest);
        await context.SaveChangesAsync();

        TenantTaxProfileRepository repository = new TenantTaxProfileRepository(context);

        // Act
        repository.Remove(latest);
        await repository.SaveChangesAsync(CancellationToken.None);

        // Assert
        IReadOnlyList<TenantTaxProfile> remaining = await repository.GetByTenantAsync(tenantId, CancellationToken.None);
        remaining.Should().ContainSingle();
        remaining[0].Id.Should().Be(older.Id);
    }

    [Fact]
    public async Task SaveChangesAsync_WithoutTenantContext_ThrowsAndWritesNothing()
    {
        // Arrange
        // TenantTaxProfile is ITenantScoped, so saving it from a context with no resolved
        // tenant must fail loudly instead of creating an orphan row no tenant can see.
        Guid tenantId = Guid.NewGuid();

        using IdentityDbContext context = BuildContext(BuildOptions(), tenantId: null);
        TenantTaxProfileRepository repository = new TenantTaxProfileRepository(context);

        repository.Add(BuildProfile(tenantId, new DateOnly(2026, 1, 1)));

        // Act
        Func<Task> act = () => repository.SaveChangesAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<System.Security.SecurityException>();
    }
}
