using Delobytes.App.Backend.Identity.Application.Commands.CreateTenant;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Application.Options;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Domain.Enums;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Commands;

public class CreateTenantCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<ITenantRepository> _tenantRepositoryMock;
    private readonly Mock<ITenantMembershipRepository> _membershipRepositoryMock;
    private readonly Mock<IJwtTokenService> _jwtTokenServiceMock;
    private readonly Mock<IOptions<MultitenancyOptions>> _optionsMock;
    private readonly CreateTenantCommandHandler _handler;

    public CreateTenantCommandHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _tenantRepositoryMock = new Mock<ITenantRepository>();
        _membershipRepositoryMock = new Mock<ITenantMembershipRepository>();
        _jwtTokenServiceMock = new Mock<IJwtTokenService>();

        _optionsMock = new Mock<IOptions<MultitenancyOptions>>();
        _optionsMock.Setup(o => o.Value).Returns(new MultitenancyOptions
        {
            MaxTenantsPerUser = 5
        });

        _handler = new CreateTenantCommandHandler(
            _userRepositoryMock.Object,
            _tenantRepositoryMock.Object,
            _membershipRepositoryMock.Object,
            _jwtTokenServiceMock.Object,
            _optionsMock.Object);
    }

    [Fact]
    public async Task Handle_FirstTenantCreation_Succeeds()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        string tenantName = "My First Company";
        string expectedToken = "jwt-token-123";

        User user = new User
        {
            Id = userId,
            Email = "user@example.com",
            PasswordHash = "hash"
        };

        _userRepositoryMock
            .Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _membershipRepositoryMock
            .Setup(r => r.CountActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _jwtTokenServiceMock
            .Setup(s => s.GenerateToken(userId, It.IsAny<Guid>(), Role.Administrator))
            .Returns(expectedToken);

        CreateTenantCommand command = new CreateTenantCommand
        {
            UserId = userId,
            TenantName = tenantName,
            CurrentTenantId = null
        };

        // Act
        CreateTenantResponse response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotEqual(Guid.Empty, response.TenantId);
        Assert.Equal(expectedToken, response.AccessToken);

        _tenantRepositoryMock.Verify(
            r => r.Add(It.Is<Tenant>(t => t.Name == tenantName && t.IsActive)),
            Times.Once);

        _membershipRepositoryMock.Verify(
            r => r.Add(It.Is<TenantMembership>(m => 
                m.UserId == userId && 
                m.Role == Role.Administrator && 
                m.IsActive)),
            Times.Once);

        _tenantRepositoryMock.Verify(
            r => r.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_AdditionalTenantByAdministrator_Succeeds()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid currentTenantId = Guid.NewGuid();
        string tenantName = "My Second Company";
        string expectedToken = "jwt-token-456";

        User user = new User
        {
            Id = userId,
            Email = "admin@example.com",
            PasswordHash = "hash"
        };

        TenantMembership currentMembership = new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = currentTenantId,
            Role = Role.Administrator,
            IsActive = true
        };

        _userRepositoryMock
            .Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _membershipRepositoryMock
            .Setup(r => r.CountActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _membershipRepositoryMock
            .Setup(r => r.FindActiveByUserAndTenantAsync(userId, currentTenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentMembership);

        _jwtTokenServiceMock
            .Setup(s => s.GenerateToken(userId, It.IsAny<Guid>(), Role.Administrator))
            .Returns(expectedToken);

        CreateTenantCommand command = new CreateTenantCommand
        {
            UserId = userId,
            TenantName = tenantName,
            CurrentTenantId = currentTenantId
        };

        // Act
        CreateTenantResponse response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotEqual(Guid.Empty, response.TenantId);
        Assert.Equal(expectedToken, response.AccessToken);

        _tenantRepositoryMock.Verify(
            r => r.Add(It.Is<Tenant>(t => t.Name == tenantName)),
            Times.Once);

        _membershipRepositoryMock.Verify(
            r => r.Add(It.Is<TenantMembership>(m => m.Role == Role.Administrator)),
            Times.Once);
    }

    [Fact]
    public async Task Handle_AdditionalTenantByNonAdministrator_ThrowsException()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid currentTenantId = Guid.NewGuid();

        User user = new User
        {
            Id = userId,
            Email = "manager@example.com",
            PasswordHash = "hash"
        };

        TenantMembership currentMembership = new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = currentTenantId,
            Role = Role.Manager,
            IsActive = true
        };

        _userRepositoryMock
            .Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _membershipRepositoryMock
            .Setup(r => r.CountActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _membershipRepositoryMock
            .Setup(r => r.FindActiveByUserAndTenantAsync(userId, currentTenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentMembership);

        CreateTenantCommand command = new CreateTenantCommand
        {
            UserId = userId,
            TenantName = "Unauthorized Tenant",
            CurrentTenantId = currentTenantId
        };

        // Act & Assert
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.Handle(command, CancellationToken.None));

        Assert.Contains("Администратор", exception.Message);

        _tenantRepositoryMock.Verify(
            r => r.Add(It.IsAny<Tenant>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ExceedsMaxTenantsLimit_ThrowsException()
    {
        // Arrange
        Guid userId = Guid.NewGuid();

        User user = new User
        {
            Id = userId,
            Email = "poweruser@example.com",
            PasswordHash = "hash"
        };

        _userRepositoryMock
            .Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _membershipRepositoryMock
            .Setup(r => r.CountActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        CreateTenantCommand command = new CreateTenantCommand
        {
            UserId = userId,
            TenantName = "Too Many Tenants",
            CurrentTenantId = Guid.NewGuid()
        };

        // Act & Assert
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.Handle(command, CancellationToken.None));

        Assert.Contains("максимальный лимит", exception.Message);

        _tenantRepositoryMock.Verify(
            r => r.Add(It.IsAny<Tenant>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_UserNotFound_ThrowsException()
    {
        // Arrange
        Guid userId = Guid.NewGuid();

        _userRepositoryMock
            .Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        CreateTenantCommand command = new CreateTenantCommand
        {
            UserId = userId,
            TenantName = "Test Tenant",
            CurrentTenantId = null
        };

        // Act & Assert
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.Handle(command, CancellationToken.None));

        Assert.Contains("не найден", exception.Message);
    }

    [Fact]
    public async Task Handle_AdditionalTenantWithoutCurrentTenantId_ThrowsException()
    {
        // Arrange
        Guid userId = Guid.NewGuid();

        User user = new User
        {
            Id = userId,
            Email = "user@example.com",
            PasswordHash = "hash"
        };

        _userRepositoryMock
            .Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _membershipRepositoryMock
            .Setup(r => r.CountActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateTenantCommand command = new CreateTenantCommand
        {
            UserId = userId,
            TenantName = "Second Tenant",
            CurrentTenantId = null
        };

        // Act & Assert
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.Handle(command, CancellationToken.None));

        Assert.Contains("текущий активный тенант", exception.Message);
    }
}
