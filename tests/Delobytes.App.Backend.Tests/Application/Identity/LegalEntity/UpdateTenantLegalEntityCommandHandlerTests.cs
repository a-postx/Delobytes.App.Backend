using Delobytes.App.Backend.Identity.Application.Commands.UpdateTenantLegalEntity;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Domain.Enums;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity.LegalEntity;

/// <summary>
/// Tests for UpdateTenantLegalEntityCommandHandler (stage 10).
/// </summary>
public class UpdateTenantLegalEntityCommandHandlerTests
{
    private readonly Mock<ITenantRepository> _tenantRepositoryMock;
    private readonly UpdateTenantLegalEntityCommandHandler _handler;

    public UpdateTenantLegalEntityCommandHandlerTests()
    {
        _tenantRepositoryMock = new Mock<ITenantRepository>();
        _handler = new UpdateTenantLegalEntityCommandHandler(_tenantRepositoryMock.Object);
    }

    /// <summary>
    /// Creates a tenant whose tax settings are not yet configured: the enum-backed
    /// properties keep the value 0, which matches no declared enum member.
    /// </summary>
    private static Tenant BuildUnconfiguredTenant(Guid tenantId) => new Tenant
    {
        Id = tenantId,
        Name = "Existing Tenant",
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
        UpdatedAt = null,
        IsActive = true,
    };

    private void SetupTenant(Tenant tenant)
    {
        _tenantRepositoryMock
            .Setup(x => x.FindByIdAsync(tenant.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        _tenantRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsAllFiveLegalEntityProperties()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Tenant tenant = BuildUnconfiguredTenant(tenantId);
        SetupTenant(tenant);

        UpdateTenantLegalEntityCommand command = new UpdateTenantLegalEntityCommand
        {
            TenantId = tenantId,
            LegalName = "ООО «Ромашка»",
            Inn = "7712345678",
            TaxType = TaxType.Usn,
            TaxRatePercent = 6m,
            VatType = VatType.None,
        };

        // Act
        UpdateTenantLegalEntityResponse response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal(tenantId, response.TenantId);
        Assert.Equal("ООО «Ромашка»", response.LegalName);
        Assert.Equal("7712345678", response.Inn);
        Assert.Equal(TaxType.Usn, response.TaxType);
        Assert.Equal(6m, response.TaxRatePercent);
        Assert.Equal(VatType.None, response.VatType);

        Assert.Equal("ООО «Ромашка»", tenant.LegalName);
        Assert.Equal("7712345678", tenant.Inn);
        Assert.Equal(TaxType.Usn, tenant.TaxType);
        Assert.Equal(6m, tenant.TaxRatePercent);
        Assert.Equal(VatType.None, tenant.VatType);

        _tenantRepositoryMock.Verify(x => x.Update(tenant), Times.Once);
        _tenantRepositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidCommand_SetsUpdatedAtTimestamp()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Tenant tenant = BuildUnconfiguredTenant(tenantId);
        SetupTenant(tenant);
        DateTimeOffset beforeUpdate = DateTimeOffset.UtcNow;

        UpdateTenantLegalEntityCommand command = new UpdateTenantLegalEntityCommand
        {
            TenantId = tenantId,
            TaxType = TaxType.Osno,
            TaxRatePercent = 20m,
            VatType = VatType.TwentyTwo,
        };

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(tenant.UpdatedAt);
        Assert.True(tenant.UpdatedAt >= beforeUpdate);
        Assert.True(tenant.UpdatedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Handle_TenantNotFound_ThrowsInvalidOperationExceptionWithoutSaving()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();

        _tenantRepositoryMock
            .Setup(x => x.FindByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        UpdateTenantLegalEntityCommand command = new UpdateTenantLegalEntityCommand
        {
            TenantId = tenantId,
            TaxType = TaxType.Usn,
            TaxRatePercent = 6m,
            VatType = VatType.None,
        };

        // Act & Assert
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.Handle(command, CancellationToken.None));

        Assert.Contains(tenantId.ToString(), exception.Message);
        Assert.Contains("не найдено", exception.Message);

        _tenantRepositoryMock.Verify(x => x.Update(It.IsAny<Tenant>()), Times.Never);
        _tenantRepositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NullOptionalTextFields_ClearsLegalNameAndInn()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Tenant tenant = BuildUnconfiguredTenant(tenantId);
        tenant.LegalName = "Ранее заполненное имя";
        tenant.Inn = "7701234567";
        SetupTenant(tenant);

        UpdateTenantLegalEntityCommand command = new UpdateTenantLegalEntityCommand
        {
            TenantId = tenantId,
            LegalName = null,
            Inn = null,
            TaxType = TaxType.Npd,
            TaxRatePercent = 6m,
            VatType = VatType.None,
        };

        // Act
        UpdateTenantLegalEntityResponse response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Null(response.LegalName);
        Assert.Null(response.Inn);
        Assert.Null(tenant.LegalName);
        Assert.Null(tenant.Inn);
    }

    [Theory]
    [InlineData(TaxType.Usn, 6, VatType.None)]
    [InlineData(TaxType.Osno, 20, VatType.TwentyTwo)]
    [InlineData(TaxType.Npd, 4, VatType.None)]
    [InlineData(TaxType.Osno, 5, VatType.Five)]
    [InlineData(TaxType.Osno, 7, VatType.Seven)]
    [InlineData(TaxType.Usn, 0, VatType.None)]
    [InlineData(TaxType.Usn, 100, VatType.TwentyTwo)]
    public async Task Handle_EveryDeclaredTaxCombination_IsPersisted(
        TaxType taxType,
        decimal taxRatePercent,
        VatType vatType)
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Tenant tenant = BuildUnconfiguredTenant(tenantId);
        SetupTenant(tenant);

        UpdateTenantLegalEntityCommand command = new UpdateTenantLegalEntityCommand
        {
            TenantId = tenantId,
            TaxType = taxType,
            TaxRatePercent = taxRatePercent,
            VatType = vatType,
        };

        // Act
        UpdateTenantLegalEntityResponse response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal(taxType, response.TaxType);
        Assert.Equal(taxRatePercent, response.TaxRatePercent);
        Assert.Equal(vatType, response.VatType);
        Assert.Equal(taxType, tenant.TaxType);
        Assert.Equal(taxRatePercent, tenant.TaxRatePercent);
        Assert.Equal(vatType, tenant.VatType);
    }

    [Fact]
    public async Task Handle_Command_DoesNotTouchOtherTenantProperties()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Tenant tenant = BuildUnconfiguredTenant(tenantId);
        string originalName = tenant.Name;
        DateTimeOffset originalCreatedAt = tenant.CreatedAt;
        SetupTenant(tenant);

        UpdateTenantLegalEntityCommand command = new UpdateTenantLegalEntityCommand
        {
            TenantId = tenantId,
            LegalName = "ООО «Лютик»",
            TaxType = TaxType.Usn,
            TaxRatePercent = 6m,
            VatType = VatType.None,
        };

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal(originalName, tenant.Name);
        Assert.Equal(originalCreatedAt, tenant.CreatedAt);
        Assert.True(tenant.IsActive);
    }
}
