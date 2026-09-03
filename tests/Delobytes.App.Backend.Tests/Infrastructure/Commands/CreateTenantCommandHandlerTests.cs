using Delobytes.App.Backend.Identity.Application.Commands.CreateTenant;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Application.Options;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Delobytes.App.Backend.Tests.Identity;

/// <summary>
/// Tests for CreateTenantCommandHandler.
/// </summary>
public class CreateTenantCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepo;
    private readonly Mock<ITenantRepository> _tenantRepo;
    private readonly Mock<ITenantMembershipRepository> _membershipRepo;
    private readonly Mock<IJwtTokenService> _jwtService;
    private readonly Mock<IOptions<MultitenancyOptions>> _options;

    public CreateTenantCommandHandlerTests()
    {
        _userRepo = new Mock<IUserRepository>();
        _tenantRepo = new Mock<ITenantRepository>();
        _membershipRepo = new Mock<ITenantMembershipRepository>();
        _jwtService = new Mock<IJwtTokenService>();
        _options = new Mock<IOptions<MultitenancyOptions>>();

        _options.Setup(o => o.Value).Returns(new MultitenancyOptions { MaxTenantsPerUser = 5 });
    }

    private CreateTenantCommandHandler BuildHandler()
        => new CreateTenantCommandHandler(
            _userRepo.Object,
            _tenantRepo.Object,
            _membershipRepo.Object,
            _jwtService.Object,
            _options.Object);

    [Fact]
    public async Task Handle_ValidCommand_FirstTenant_CreatesTenantAndMembership()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            ExternalId = "test@example.com",
            IdentityProvider = "Local",
            Email = "test@example.com",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };

        _userRepo.Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _membershipRepo.Setup(r => r.CountActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _tenantRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _jwtService.Setup(s => s.GenerateToken(userId, It.IsAny<Guid>(), Role.Administrator))
            .Returns("fake-jwt-token");

        Tenant? capturedTenant = null;
        _tenantRepo.Setup(r => r.Add(It.IsAny<Tenant>()))
            .Callback<Tenant>(t => capturedTenant = t);

        TenantMembership? capturedMembership = null;
        _membershipRepo.Setup(r => r.Add(It.IsAny<TenantMembership>()))
            .Callback<TenantMembership>(m => capturedMembership = m);

        var command = new CreateTenantCommand
        {
            UserId = userId,
            TenantName = "Test Company",
            CurrentTenantId = null,
        };

        // Act
        CreateTenantResponse response = await BuildHandler().Handle(command, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.TenantId.Should().NotBeEmpty();
        response.AccessToken.Should().Be("fake-jwt-token");

        capturedTenant.Should().NotBeNull();
        capturedTenant!.Name.Should().Be("Test Company");
        capturedTenant.IsActive.Should().BeTrue();

        capturedMembership.Should().NotBeNull();
        capturedMembership!.UserId.Should().Be(userId);
        capturedMembership.TenantId.Should().Be(capturedTenant.Id);
        capturedMembership.Role.Should().Be(Role.Administrator);
        capturedMembership.IsActive.Should().BeTrue();

        _tenantRepo.Verify(r => r.Add(It.IsAny<Tenant>()), Times.Once);
        _membershipRepo.Verify(r => r.Add(It.IsAny<TenantMembership>()), Times.Once);
        _tenantRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AdministratorCreatesAdditionalTenant_Success()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var currentTenantId = Guid.NewGuid();

        var user = new User
        {
            Id = userId,
            ExternalId = "admin@example.com",
            IdentityProvider = "Local",
            Email = "admin@example.com",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };

        var currentMembership = new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = currentTenantId,
            Role = Role.Administrator,
            IsActive = true,
        };

        _userRepo.Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _membershipRepo.Setup(r => r.CountActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _membershipRepo.Setup(r => r.FindActiveByUserAndTenantAsync(userId, currentTenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentMembership);

        _tenantRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _jwtService.Setup(s => s.GenerateToken(userId, It.IsAny<Guid>(), Role.Administrator))
            .Returns("new-tenant-token");

        var command = new CreateTenantCommand
        {
            UserId = userId,
            TenantName = "Second Company",
            CurrentTenantId = currentTenantId,
        };

        // Act
        CreateTenantResponse response = await BuildHandler().Handle(command, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.TenantId.Should().NotBeEmpty();
        response.AccessToken.Should().Be("new-tenant-token");

        _membershipRepo.Verify(r => r.FindActiveByUserAndTenantAsync(userId, currentTenantId, It.IsAny<CancellationToken>()), Times.Once);
        _tenantRepo.Verify(r => r.Add(It.IsAny<Tenant>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UserNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userRepo.Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var command = new CreateTenantCommand { UserId = userId, TenantName = "Test Company" };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => BuildHandler().Handle(command, CancellationToken.None));

        _tenantRepo.Verify(r => r.Add(It.IsAny<Tenant>()), Times.Never);
        _membershipRepo.Verify(r => r.Add(It.IsAny<TenantMembership>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UserExceedsMaxTenants_ThrowsInvalidOperationException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            ExternalId = "test@example.com",
            IdentityProvider = "Local",
            Email = "test@example.com",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };

        _userRepo.Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _membershipRepo.Setup(r => r.CountActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        var command = new CreateTenantCommand
        {
            UserId = userId,
            TenantName = "Sixth Tenant",
            CurrentTenantId = Guid.NewGuid(),
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => BuildHandler().Handle(command, CancellationToken.None));

        _tenantRepo.Verify(r => r.Add(It.IsAny<Tenant>()), Times.Never);
        _membershipRepo.Verify(r => r.Add(It.IsAny<TenantMembership>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ManagerTriesToCreateAdditionalTenant_ThrowsInvalidOperationException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var currentTenantId = Guid.NewGuid();

        var user = new User
        {
            Id = userId,
            ExternalId = "manager@example.com",
            IdentityProvider = "Local",
            Email = "manager@example.com",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };

        var currentMembership = new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = currentTenantId,
            Role = Role.Manager,
            IsActive = true,
        };

        _userRepo.Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _membershipRepo.Setup(r => r.CountActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _membershipRepo.Setup(r => r.FindActiveByUserAndTenantAsync(userId, currentTenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentMembership);

        var command = new CreateTenantCommand
        {
            UserId = userId,
            TenantName = "Unauthorized Tenant",
            CurrentTenantId = currentTenantId,
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => BuildHandler().Handle(command, CancellationToken.None));

        _tenantRepo.Verify(r => r.Add(It.IsAny<Tenant>()), Times.Never);
        _membershipRepo.Verify(r => r.Add(It.IsAny<TenantMembership>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReadOnlyUserTriesToCreateAdditionalTenant_ThrowsInvalidOperationException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var currentTenantId = Guid.NewGuid();

        var user = new User
        {
            Id = userId,
            ExternalId = "readonly@example.com",
            IdentityProvider = "Local",
            Email = "readonly@example.com",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };

        var currentMembership = new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = currentTenantId,
            Role = Role.ReadOnly,
            IsActive = true,
        };

        _userRepo.Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _membershipRepo.Setup(r => r.CountActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _membershipRepo.Setup(r => r.FindActiveByUserAndTenantAsync(userId, currentTenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentMembership);

        var command = new CreateTenantCommand
        {
            UserId = userId,
            TenantName = "Unauthorized Tenant",
            CurrentTenantId = currentTenantId,
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => BuildHandler().Handle(command, CancellationToken.None));

        _tenantRepo.Verify(r => r.Add(It.IsAny<Tenant>()), Times.Never);
        _membershipRepo.Verify(r => r.Add(It.IsAny<TenantMembership>()), Times.Never);
    }
}
