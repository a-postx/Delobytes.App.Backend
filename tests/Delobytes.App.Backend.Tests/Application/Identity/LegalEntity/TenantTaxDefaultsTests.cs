using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Identity.Application.Commands.CreateTenant;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Application.Options;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Domain.Enums;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity.LegalEntity;

/// <summary>
/// Tests the stage-10 requirement that tax rates are never filled in automatically:
/// a freshly created tenant must not silently acquire a tax regime or a VAT mode.
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

    [Fact]
    public async Task CreateTenant_LeavesTaxRegimeAndVatUnset()
    {
        // Arrange
        Guid userId = Guid.NewGuid();

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

        // The sentinel values below match no declared enum member, which is what the
        // frontend relies on to render an em dash instead of a preselected regime.
        Assert.False(
            Enum.IsDefined(typeof(TaxType), createdTenant!.TaxType),
            $"Newly created tenant must not default TaxType, but got {createdTenant.TaxType}.");
        Assert.False(
            Enum.IsDefined(typeof(VatType), createdTenant.VatType),
            $"Newly created tenant must not default VatType, but got {createdTenant.VatType}.");
        Assert.Equal(0m, createdTenant.TaxRatePercent);
    }

    [Fact]
    public async Task CreateTenant_LeavesLegalEntityDetailsNull()
    {
        // Arrange
        Guid userId = Guid.NewGuid();

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
