using Delobytes.App.Backend.Identity.Application.Commands;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity.Settings;

/// <summary>
/// Tests for UpdateTenantSettingsCommandHandler.
/// </summary>
public class UpdateTenantSettingsCommandHandlerTests
{
    private readonly Mock<ITenantRepository> _tenantRepositoryMock;
    private readonly UpdateTenantSettingsCommandHandler _handler;

    public UpdateTenantSettingsCommandHandlerTests()
    {
        _tenantRepositoryMock = new Mock<ITenantRepository>();
        _handler = new UpdateTenantSettingsCommandHandler(_tenantRepositoryMock.Object);
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
    public async Task Handle_ValidCommand_PersistsTimeZone()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Tenant tenant = BuildTenant(tenantId);
        SetupTenant(tenant);

        UpdateTenantSettingsCommand command = new UpdateTenantSettingsCommand
        {
            TenantId = tenantId,
            TimeZone = "Asia/Vladivostok",
        };

        // Act
        UpdateTenantSettingsResponse response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal(tenantId, response.TenantId);
        Assert.Equal("Asia/Vladivostok", response.TimeZone);
        Assert.Equal("Asia/Vladivostok", tenant.TimeZone);

        _tenantRepositoryMock.Verify(x => x.Update(tenant), Times.Once);
        _tenantRepositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TimeZoneWithSurroundingSpaces_SavesTrimmedValue()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Tenant tenant = BuildTenant(tenantId);
        SetupTenant(tenant);

        UpdateTenantSettingsCommand command = new UpdateTenantSettingsCommand
        {
            TenantId = tenantId,
            TimeZone = "  Europe/Samara  ",
        };

        // Act
        UpdateTenantSettingsResponse response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal("Europe/Samara", response.TimeZone);
        Assert.Equal("Europe/Samara", tenant.TimeZone);
    }

    [Fact]
    public async Task Handle_ValidCommand_SetsUpdatedAtTimestamp()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Tenant tenant = BuildTenant(tenantId);
        SetupTenant(tenant);
        DateTimeOffset beforeUpdate = DateTimeOffset.UtcNow;

        UpdateTenantSettingsCommand command = new UpdateTenantSettingsCommand
        {
            TenantId = tenantId,
            TimeZone = "Asia/Vladivostok",
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

        UpdateTenantSettingsCommand command = new UpdateTenantSettingsCommand
        {
            TenantId = tenantId,
            TimeZone = "Asia/Vladivostok",
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
    public async Task Handle_Command_DoesNotTouchOtherTenantProperties()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Tenant tenant = BuildTenant(tenantId);
        tenant.Currency = "RUB";
        tenant.MaxBackgroundJobsParallelism = 7;
        string originalName = tenant.Name;
        DateTimeOffset originalCreatedAt = tenant.CreatedAt;
        SetupTenant(tenant);

        UpdateTenantSettingsCommand command = new UpdateTenantSettingsCommand
        {
            TenantId = tenantId,
            TimeZone = "Asia/Vladivostok",
        };

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal(originalName, tenant.Name);
        Assert.Equal(originalCreatedAt, tenant.CreatedAt);
        Assert.Equal("RUB", tenant.Currency);
        Assert.Equal(7, tenant.MaxBackgroundJobsParallelism);
        Assert.True(tenant.IsActive);
    }
}
