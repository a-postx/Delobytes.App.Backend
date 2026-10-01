using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Application.Queries.GetTenantLegalEntity;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity.LegalEntity;

/// <summary>
/// Tests for GetTenantLegalEntityQueryHandler.
///
/// The response no longer contains tax fields — they moved to the tax profile — so these
/// tests pin only the legal entity payload, plus the guarantee that reading it performs
/// exactly one lookup.
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
    public async Task Handle_ConfiguredTenant_ReturnsAllLegalEntityProperties()
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
    }

    [Fact]
    public async Task Handle_TenantWithoutLegalEntityDetails_SubstitutesNoDefault()
    {
        // Arrange
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
            LegalName = "ООО «Ромашка»",
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
        Assert.NotEqual(otherTenantId, response.TenantId);

        _tenantRepositoryMock.Verify(
            x => x.FindByIdAsync(requestedTenantId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("ООО «Ромашка»", "7712345678")]
    [InlineData("ИП Иванов И.И.", "771234567890")]
    [InlineData(null, null)]
    public async Task Handle_EveryLegalEntityShape_IsReturnedUnchanged(string? legalName, string? inn)
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Tenant tenant = new Tenant
        {
            Id = tenantId,
            Name = "Tenant",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
            LegalName = legalName,
            Inn = inn,
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
        Assert.Equal(legalName, response.LegalName);
        Assert.Equal(inn, response.Inn);
    }
}
