using Delobytes.App.Backend.Identity.Application.Commands.CreateTenant;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Domain.Enums;
using FluentAssertions;
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

    public CreateTenantCommandHandlerTests()
    {
        _userRepo = new Mock<IUserRepository>();
        _tenantRepo = new Mock<ITenantRepository>();
        _membershipRepo = new Mock<ITenantMembershipRepository>();
        _jwtService = new Mock<IJwtTokenService>();
    }

    private CreateTenantCommandHandler BuildHandler()
        => new CreateTenantCommandHandler(
            _userRepo.Object,
            _tenantRepo.Object,
            _membershipRepo.Object,
            _jwtService.Object);

    [Fact]
    public async Task Handle_ValidCommand_CreatesTenantAndMembership()
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

        _membershipRepo.Setup(r => r.ExistsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

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

        var command = new CreateTenantCommand { UserId = userId, TenantName = "Test Company" };

        // Act
        var response = await BuildHandler().Handle(command, CancellationToken.None);

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
    public async Task Handle_UserAlreadyHasTenant_ThrowsInvalidOperationException()
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

        _membershipRepo.Setup(r => r.ExistsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var command = new CreateTenantCommand { UserId = userId, TenantName = "New Tenant" };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => BuildHandler().Handle(command, CancellationToken.None));

        _tenantRepo.Verify(r => r.Add(It.IsAny<Tenant>()), Times.Never);
        _membershipRepo.Verify(r => r.Add(It.IsAny<TenantMembership>()), Times.Never);
    }
}
