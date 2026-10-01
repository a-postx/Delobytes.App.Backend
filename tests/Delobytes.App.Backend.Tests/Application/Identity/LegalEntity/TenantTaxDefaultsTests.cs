using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Identity.Application.Commands.CreateTenant;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Application.Options;
using Delobytes.App.Backend.Identity.Domain.Constants;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
namespace Delobytes.App.Backend.Tests.Application.Identity.LegalEntity;

/// <summary>
/// Tests that a freshly created tenant never silently acquires a tax regime.
///
/// The old model kept the tax settings on Tenant itself and relied on sentinel enum
/// values (0, matching no declared member) to mean "not chosen". That sentinel is gone:
/// TenantTaxProfile no longer exists for a new tenant, and the absence of rows is the
/// single representation of "tax is not configured".
/// </summary>
public class TenantTaxDefaultsTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<ITenantRepository> _tenantRepositoryMock;
    private readonly Mock<ITenantMembershipRepository> _membershipRepositoryMock;
    private readonly Mock<IJwtTokenService> _jwtTokenServiceMock;
    private readonly CreateTenantCommandHandler _handler;

    public TenantTaxDefaultsTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _tenantRepositoryMock = new Mock<ITenantRepository>();
        _membershipRepositoryMock = new Mock<ITenantMembershipRepository>();
        _jwtTokenServiceMock = new Mock<IJwtTokenService>();

        Mock<IOptions<MultitenancyOptions>> optionsMock = new Mock<IOptions<MultitenancyOptions>>();
        optionsMock.Setup(o => o.Value).Returns(new MultitenancyOptions
        {
            MaxTenantsPerUser = 5,
        });

        _handler = new CreateTenantCommandHandler(
            _userRepositoryMock.Object,
            _tenantRepositoryMock.Object,
            _membershipRepositoryMock.Object,
            _jwtTokenServiceMock.Object,
            optionsMock.Object);
    }

    private void SetupFirstTenantCreation(Guid userId)
    {
        _userRepositoryMock
            .Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = userId,
                Email = "user@example.com",
                PasswordHash = "hash",
            });

        _membershipRepositoryMock
            .Setup(r => r.CountActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _jwtTokenServiceMock
            .Setup(s => s.GenerateToken(userId, It.IsAny<Guid>(), Role.Administrator))
            .Returns("jwt-token");
    }

    [Fact]
    public async Task CreateTenant_ProducesATenantWithNoTaxConfigurationAtAll()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        SetupFirstTenantCreation(userId);

        Tenant? createdTenant = null;

        _tenantRepositoryMock
            .Setup(r => r.Add(It.IsAny<Tenant>()))
            .Callback<Tenant>(tenant => createdTenant = tenant);

        CreateTenantCommand command = new CreateTenantCommand
        {
            UserId = userId,
            TenantName = "My First Company",
            CurrentTenantId = null,
        };

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(createdTenant);

        // "Tax not configured" is now expressed by the absence of TenantTaxProfile rows.
        // Tenant itself no longer has any tax member the creation flow could accidentally
        // set, so there is nothing to leave at a sentinel default here.
        Assert.Empty(
            typeof(Tenant).GetProperties()
                .Where(p => p.Name.Contains("Tax", StringComparison.Ordinal))
                .Select(p => p.Name));
    }

    [Fact]
    public async Task CreateTenant_LeavesCurrencyAndTimeZoneAtTheirDefaults()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        SetupFirstTenantCreation(userId);

        Tenant? createdTenant = null;

        _tenantRepositoryMock
            .Setup(r => r.Add(It.IsAny<Tenant>()))
            .Callback<Tenant>(tenant => createdTenant = tenant);

        CreateTenantCommand command = new CreateTenantCommand
        {
            UserId = userId,
            TenantName = "My First Company",
            CurrentTenantId = null,
        };

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(createdTenant);
        Assert.Equal(TenantCurrencies.Rub, createdTenant!.Currency);
        Assert.Equal("Europe/Moscow", createdTenant.TimeZone);
    }

    [Fact]
    public async Task CreateTenant_LeavesLegalEntityDetailsNull()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        SetupFirstTenantCreation(userId);

        Tenant? createdTenant = null;

        _tenantRepositoryMock
            .Setup(r => r.Add(It.IsAny<Tenant>()))
            .Callback<Tenant>(tenant => createdTenant = tenant);

        CreateTenantCommand command = new CreateTenantCommand
        {
            UserId = userId,
            TenantName = "My First Company",
            CurrentTenantId = null,
        };

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(createdTenant);
        Assert.Null(createdTenant!.LegalName);
        Assert.Null(createdTenant.Inn);
    }
}
