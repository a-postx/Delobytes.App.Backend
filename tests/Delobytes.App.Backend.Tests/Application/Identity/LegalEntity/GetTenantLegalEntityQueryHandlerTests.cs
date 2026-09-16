using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Application.Queries.GetTenantLegalEntity;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Domain.Enums;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity.LegalEntity;

/// <summary>
/// Tests for GetTenantLegalEntityQueryHandler (stage 10).
/// </summary>
public class GetTenantLegalEntityQueryHandlerTests
{
    private readonly Mock<ITenantRepository> _tenantRepositoryMock;
    private readonly GetTenantLegalEntityQueryHandler _handler;

    public GetTenantLegalEntityQueryHandlerTests()
    {
        _tenantRepositoryMock = new Mock<ITenantRepository>();
        _handler = new GetTenantLegalEntityQueryHandler(_tenantRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ConfiguredTenant_ReturnsAllFiveLegalEntityProperties()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Tenant tenant = new Tenant
        {
            Id = tenantId,
            Name = "Tenant",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            IsActive = true,
            LegalName = "ООО «Ромашка»",
            Inn = "7712345678",
            TaxType = TaxType.Usn,
            TaxRatePercent = 6m,
            VatType = VatType.None,
        };

        _tenantRepositoryMock
            .Setup(x => x.FindByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        GetTenantLegalEntityQuery query = new GetTenantLegalEntityQuery
        {
            TenantId = tenantId,
        };

        // Act
        GetTenantLegalEntityResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.Equal(tenantId, response.TenantId);
        Assert.Equal("ООО «Ромашка»", response.LegalName);
        Assert.Equal("7712345678", response.Inn);
        Assert.Equal(TaxType.Usn, response.TaxType);
        Assert.Equal(6m, response.TaxRatePercent);
        Assert.Equal(VatType.None, response.VatType);
    }

    [Fact]
    public async Task Handle_TenantWithoutConfiguredTaxSettings_DoesNotSubstituteAnyDefault()
    {
        // Arrange
        // A freshly created tenant has never had its tax settings set, so the
        // enum-backed and decimal properties keep their CLR defaults.
        Guid tenantId = Guid.NewGuid();
        Tenant tenant = new Tenant
        {
            Id = tenantId,
            Name = "Fresh Tenant",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };

        _tenantRepositoryMock
            .Setup(x => x.FindByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        GetTenantLegalEntityQuery query = new GetTenantLegalEntityQuery
        {
            TenantId = tenantId,
        };

        // Act
        GetTenantLegalEntityResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.Null(response.LegalName);
        Assert.Null(response.Inn);
        Assert.Equal(0m, response.TaxRatePercent);
        Assert.False(Enum.IsDefined(typeof(TaxType), response.TaxType));
        Assert.False(Enum.IsDefined(typeof(VatType), response.VatType));
    }

    [Fact]
    public async Task Handle_TenantNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();

        _tenantRepositoryMock
            .Setup(x => x.FindByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        GetTenantLegalEntityQuery query = new GetTenantLegalEntityQuery
        {
            TenantId = tenantId,
        };

        // Act & Assert
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.Handle(query, CancellationToken.None));

        Assert.Contains(tenantId.ToString(), exception.Message);
        Assert.Contains("не найдено", exception.Message);
    }

    [Fact]
    public async Task Handle_Query_LooksUpExactlyTheRequestedTenant()
    {
        // Arrange
        Guid requestedTenantId = Guid.NewGuid();
        Guid otherTenantId = Guid.NewGuid();

        Tenant requestedTenant = new Tenant
        {
            Id = requestedTenantId,
            Name = "Requested",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
            TaxType = TaxType.Osno,
            TaxRatePercent = 20m,
            VatType = VatType.TwentyTwo,
        };

        _tenantRepositoryMock
            .Setup(x => x.FindByIdAsync(requestedTenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(requestedTenant);

        GetTenantLegalEntityQuery query = new GetTenantLegalEntityQuery
        {
            TenantId = requestedTenantId,
        };

        // Act
        GetTenantLegalEntityResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.Equal(requestedTenantId, response.TenantId);
        Assert.Equal(TaxType.Osno, response.TaxType);
        Assert.NotEqual(otherTenantId, response.TenantId);

        _tenantRepositoryMock.Verify(
            x => x.FindByIdAsync(requestedTenantId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(TaxType.Usn, 6, VatType.None)]
    [InlineData(TaxType.Osno, 20, VatType.TwentyTwo)]
    [InlineData(TaxType.Npd, 4, VatType.None)]
    [InlineData(TaxType.Osno, 5, VatType.Five)]
    [InlineData(TaxType.Osno, 7, VatType.Seven)]
    public async Task Handle_EveryDeclaredTaxCombination_IsReturnedUnchanged(
        TaxType taxType,
        decimal taxRatePercent,
        VatType vatType)
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Tenant tenant = new Tenant
        {
            Id = tenantId,
            Name = "Tenant",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
            TaxType = taxType,
            TaxRatePercent = taxRatePercent,
            VatType = vatType,
        };

        _tenantRepositoryMock
            .Setup(x => x.FindByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        GetTenantLegalEntityQuery query = new GetTenantLegalEntityQuery
        {
            TenantId = tenantId,
        };

        // Act
        GetTenantLegalEntityResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.Equal(taxType, response.TaxType);
        Assert.Equal(taxRatePercent, response.TaxRatePercent);
        Assert.Equal(vatType, response.VatType);
    }
}
