using Delobytes.App.Backend.Identity.Application.Commands.UpdateTenantLegalEntity;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity.LegalEntity;

/// <summary>
/// Tests for UpdateTenantLegalEntityCommandHandler.
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

    private static Tenant BuildTenant(Guid tenantId) => new Tenant
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
    public async Task Handle_ValidCommand_PersistsBothLegalEntityProperties()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Tenant tenant = BuildTenant(tenantId);
        SetupTenant(tenant);

        UpdateTenantLegalEntityCommand command = new UpdateTenantLegalEntityCommand
        {
            TenantId = tenantId,
            LegalName = "ООО «Ромашка»",
            Inn = "7712345678",
        };

        // Act
        UpdateTenantLegalEntityResponse response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal(tenantId, response.TenantId);
        Assert.Equal("ООО «Ромашка»", response.LegalName);
        Assert.Equal("7712345678", response.Inn);

        Assert.Equal("ООО «Ромашка»", tenant.LegalName);
        Assert.Equal("7712345678", tenant.Inn);

        _tenantRepositoryMock.Verify(x => x.Update(tenant), Times.Once);
        _tenantRepositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidCommand_SetsUpdatedAtTimestamp()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Tenant tenant = BuildTenant(tenantId);
        SetupTenant(tenant);
        DateTimeOffset beforeUpdate = DateTimeOffset.UtcNow;

        UpdateTenantLegalEntityCommand command = new UpdateTenantLegalEntityCommand
        {
            TenantId = tenantId,
            LegalName = "ООО «Ромашка»",
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
            LegalName = "ООО «Ромашка»",
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
        Tenant tenant = BuildTenant(tenantId);
        tenant.LegalName = "Ранее заполненное имя";
        tenant.Inn = "7701234567";
        SetupTenant(tenant);

        UpdateTenantLegalEntityCommand command = new UpdateTenantLegalEntityCommand
        {
            TenantId = tenantId,
            LegalName = null,
            Inn = null,
        };

        // Act
        UpdateTenantLegalEntityResponse response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Null(response.LegalName);
        Assert.Null(response.Inn);
        Assert.Null(tenant.LegalName);
        Assert.Null(tenant.Inn);
    }

    [Fact]
    public async Task Handle_Command_DoesNotTouchOtherTenantProperties()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Tenant tenant = BuildTenant(tenantId);
        string originalName = tenant.Name;
        DateTimeOffset originalCreatedAt = tenant.CreatedAt;
        SetupTenant(tenant);

        UpdateTenantLegalEntityCommand command = new UpdateTenantLegalEntityCommand
        {
            TenantId = tenantId,
            LegalName = "ООО «Лютик»",
        };

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal(originalName, tenant.Name);
        Assert.Equal(originalCreatedAt, tenant.CreatedAt);
        Assert.True(tenant.IsActive);
    }
}
