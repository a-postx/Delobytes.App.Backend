using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Application.Queries.TaxProfiles.GetTenantTaxProfiles;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity.TaxProfiles;

/// <summary>
/// Tests for GetTenantTaxProfilesQueryHandler.
/// </summary>
public class GetTenantTaxProfilesQueryHandlerTests
{
    private readonly Mock<ITenantTaxProfileRepository> _taxProfileRepositoryMock;
    private readonly GetTenantTaxProfilesQueryHandler _handler;

    public GetTenantTaxProfilesQueryHandlerTests()
    {
        _taxProfileRepositoryMock = new Mock<ITenantTaxProfileRepository>();
        _handler = new GetTenantTaxProfilesQueryHandler(_taxProfileRepositoryMock.Object);
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
            CreatedByUserId = Guid.NewGuid(),
        };

    private void SetupProfiles(Guid tenantId, params TenantTaxProfile[] profiles)
    {
        _taxProfileRepositoryMock
            .Setup(r => r.GetByTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profiles);
    }

    [Fact]
    public async Task Handle_TenantWithoutProfiles_ReturnsEmptyList()
    {
        // Arrange
        // The absence of a tax profile is normal, so the list endpoint answers with an
        // empty collection rather than an error.
        Guid tenantId = Guid.NewGuid();
        SetupProfiles(tenantId);

        // Act
        GetTenantTaxProfilesResponse response = await _handler.Handle(
            new GetTenantTaxProfilesQuery { TenantId = tenantId },
            CancellationToken.None);

        // Assert
        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task Handle_ExistingProfiles_MapsEveryFieldOfEachVersion()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        TenantTaxProfile profile = BuildProfile(tenantId, new DateOnly(2026, 1, 1), 6m, VatType.Five);
        SetupProfiles(tenantId, profile);

        // Act
        GetTenantTaxProfilesResponse response = await _handler.Handle(
            new GetTenantTaxProfilesQuery { TenantId = tenantId },
            CancellationToken.None);

        // Assert
        TenantTaxProfileItem item = Assert.Single(response.Items);
        Assert.Equal(profile.Id, item.Id);
        Assert.Equal(TaxRegime.UsnIncome, item.Regime);
        Assert.Equal(6m, item.RatePercent);
        Assert.Equal(VatType.Five, item.Vat);
        Assert.Equal(new DateOnly(2026, 1, 1), item.ValidFrom);
        Assert.Equal(profile.CreatedAt, item.CreatedAt);
        Assert.Equal(profile.CreatedByUserId, item.CreatedByUserId);
    }

    [Fact]
    public async Task Handle_MultipleVersions_PreservesTheOrderGivenByTheRepository()
    {
        // Arrange
        // The repository orders by ValidFrom descending; the handler must not reorder,
        // because the UI shows the newest version first.
        Guid tenantId = Guid.NewGuid();
        TenantTaxProfile newest = BuildProfile(tenantId, new DateOnly(2026, 6, 1));
        TenantTaxProfile middle = BuildProfile(tenantId, new DateOnly(2026, 3, 1));
        TenantTaxProfile oldest = BuildProfile(tenantId, new DateOnly(2026, 1, 1));
        SetupProfiles(tenantId, newest, middle, oldest);

        // Act
        GetTenantTaxProfilesResponse response = await _handler.Handle(
            new GetTenantTaxProfilesQuery { TenantId = tenantId },
            CancellationToken.None);

        // Assert
        Assert.Equal(3, response.Items.Count);
        Assert.Equal(new DateOnly(2026, 6, 1), response.Items[0].ValidFrom);
        Assert.Equal(new DateOnly(2026, 3, 1), response.Items[1].ValidFrom);
        Assert.Equal(new DateOnly(2026, 1, 1), response.Items[2].ValidFrom);
    }

    [Fact]
    public async Task Handle_Query_LooksUpExactlyTheRequestedTenant()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        SetupProfiles(tenantId);

        // Act
        await _handler.Handle(new GetTenantTaxProfilesQuery { TenantId = tenantId }, CancellationToken.None);

        // Assert
        _taxProfileRepositoryMock.Verify(
            r => r.GetByTenantAsync(tenantId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_RateOfOneHundredPercent_SurvivesTheMappingToTheListItem()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        SetupProfiles(tenantId, BuildProfile(tenantId, new DateOnly(2026, 1, 1), 100m));

        // Act
        GetTenantTaxProfilesResponse response = await _handler.Handle(
            new GetTenantTaxProfilesQuery { TenantId = tenantId },
            CancellationToken.None);

        // Assert
        Assert.Equal(100m, Assert.Single(response.Items).RatePercent);
    }
}
