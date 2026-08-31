using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Identity.Application.Commands.UpdateTenantName;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure.Commands;

public class UpdateTenantNameCommandHandlerTests
{
    private readonly Mock<ITenantRepository> _tenantRepositoryMock;
    private readonly UpdateTenantNameCommandHandler _handler;

    public UpdateTenantNameCommandHandlerTests()
    {
        _tenantRepositoryMock = new Mock<ITenantRepository>();
        _handler = new UpdateTenantNameCommandHandler(_tenantRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ValidTenantAndName_UpdatesTenantNameAndReturnsResponse()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        string oldName = "Old Tenant Name";
        string newName = "New Tenant Name";

        Tenant tenant = new Tenant
        {
            Id = tenantId,
            Name = oldName,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
            UpdatedAt = null,
            IsActive = true
        };

        _tenantRepositoryMock
            .Setup(x => x.FindByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        _tenantRepositoryMock
            .Setup(x => x.Update(It.IsAny<Tenant>()))
            .Verifiable();

        _tenantRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        UpdateTenantNameCommand command = new UpdateTenantNameCommand
        {
            TenantId = tenantId,
            Name = newName
        };

        // Act
        UpdateTenantNameResponse response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(tenantId, response.TenantId);
        Assert.Equal(newName, response.Name);
        Assert.Equal(newName, tenant.Name);
        Assert.NotNull(tenant.UpdatedAt);

        _tenantRepositoryMock.Verify(x => x.FindByIdAsync(tenantId, It.IsAny<CancellationToken>()), Times.Once);
        _tenantRepositoryMock.Verify(x => x.Update(tenant), Times.Once);
        _tenantRepositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TenantNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        string newName = "New Tenant Name";

        _tenantRepositoryMock
            .Setup(x => x.FindByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        UpdateTenantNameCommand command = new UpdateTenantNameCommand
        {
            TenantId = tenantId,
            Name = newName
        };

        // Act & Assert
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.Handle(command, CancellationToken.None));

        Assert.Contains(tenantId.ToString(), exception.Message);
        Assert.Contains("не найдено", exception.Message);

        _tenantRepositoryMock.Verify(x => x.FindByIdAsync(tenantId, It.IsAny<CancellationToken>()), Times.Once);
        _tenantRepositoryMock.Verify(x => x.Update(It.IsAny<Tenant>()), Times.Never);
        _tenantRepositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ValidUpdate_SetsUpdatedAtTimestamp()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        DateTimeOffset beforeUpdate = DateTimeOffset.UtcNow;

        Tenant tenant = new Tenant
        {
            Id = tenantId,
            Name = "Old Name",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-5),
            UpdatedAt = null,
            IsActive = true
        };

        _tenantRepositoryMock
            .Setup(x => x.FindByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        _tenantRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        UpdateTenantNameCommand command = new UpdateTenantNameCommand
        {
            TenantId = tenantId,
            Name = "New Name"
        };

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(tenant.UpdatedAt);
        Assert.True(tenant.UpdatedAt >= beforeUpdate);
        Assert.True(tenant.UpdatedAt <= DateTimeOffset.UtcNow);
    }

    [Theory]
    [InlineData("New Tenant Name")]
    [InlineData("Пространство №1")]
    [InlineData("テナント名")]
    [InlineData("A")]
    [InlineData("Very Long Tenant Name With Many Characters That Should Still Work")]
    public async Task Handle_VariousValidNames_UpdatesSuccessfully(string newName)
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();

        Tenant tenant = new Tenant
        {
            Id = tenantId,
            Name = "Old Name",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            IsActive = true
        };

        _tenantRepositoryMock
            .Setup(x => x.FindByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        _tenantRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        UpdateTenantNameCommand command = new UpdateTenantNameCommand
        {
            TenantId = tenantId,
            Name = newName
        };

        // Act
        UpdateTenantNameResponse response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal(newName, response.Name);
        Assert.Equal(newName, tenant.Name);
    }
}
