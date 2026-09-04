using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Application.Queries.GetCurrentUser;
using Delobytes.App.Backend.Identity.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using static Yandex.Cloud.Mdb.Clickhouse.V1.Config.ClickhouseConfig.Types.ExternalDictionary.Types.Structure.Types;

namespace Delobytes.App.Backend.Tests.Identity;

/// <summary>
/// Tests for GetCurrentUserQueryHandler.
/// </summary>
public class GetCurrentUserQueryHandlerTests
{
    private readonly Mock<IUserRepository> _userRepo;
    private readonly Mock<ITenantRepository> _tenantRepo;
    private readonly Mock<ITenantMembershipRepository> _tenantMembRepo;

    public GetCurrentUserQueryHandlerTests()
    {
        _userRepo = new Mock<IUserRepository>();
        _tenantRepo = new Mock<ITenantRepository>();
        _tenantMembRepo = new Mock<ITenantMembershipRepository>();
    }

    private GetCurrentUserQueryHandler BuildHandler()
        => new GetCurrentUserQueryHandler(_userRepo.Object, _tenantRepo.Object, _tenantMembRepo.Object);

    [Fact]
    public async Task Handle_ValidUserAndTenant_ReturnsCorrectResponse()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        Guid tenantMembershipId = Guid.NewGuid();

        User user = new User
        {
            Id = userId,
            ExternalId = "user@example.com",
            IdentityProvider = "Local",
            Email = "user@example.com",
            DisplayName = "Test User",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };

        Tenant tenant = new Tenant
        {
            Id = tenantId,
            Name = "Acme Corp",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };

        TenantMembership tenantMembership = new TenantMembership
        {
            Id = tenantMembershipId,
            TenantId = tenantId,
            Tenant = tenant,
            UserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };

        _userRepo.Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _tenantRepo.Setup(r => r.FindByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        _tenantMembRepo.Setup(r => r.GetActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantMembership>
            {
                tenantMembership
            });

        _tenantMembRepo.Setup(r => r.FindActiveByUserAndTenantAsync(userId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenantMembership);

        GetCurrentUserQuery query = new GetCurrentUserQuery
        {
            UserId = userId,
            TenantId = tenantId,
        };

        // Act
        GetCurrentUserResponse response = await BuildHandler().Handle(query, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.UserId.Should().Be(userId);
        response.Email.Should().Be("user@example.com");
        response.DisplayName.Should().Be("Test User");
        response.TenantId.Should().Be(tenantId);
        response.TenantName.Should().Be("Acme Corp");
        response.Tenants.Should().NotBeNull().And.HaveCountGreaterThan(0);
    }

    [Fact]
    public async Task Handle_NullDisplayName_ReturnedAsNull()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        Guid tenantMembershipId = Guid.NewGuid();

        User user = new User
        {
            Id = userId,
            ExternalId = "user@example.com",
            IdentityProvider = "Local",
            Email = "user@example.com",
            DisplayName = null,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };

        Tenant tenant = new Tenant
        {
            Id = tenantId,
            Name = "My Tenant",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };

        TenantMembership tenantMembership = new TenantMembership
        {
            Id = tenantMembershipId,
            TenantId = tenantId,
            Tenant = tenant,
            UserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };

        _userRepo.Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _tenantRepo.Setup(r => r.FindByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        _tenantMembRepo.Setup(r => r.GetActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantMembership>
            {
                tenantMembership
            });

        _tenantMembRepo.Setup(r => r.FindActiveByUserAndTenantAsync(userId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenantMembership);

        GetCurrentUserQuery query = new GetCurrentUserQuery { UserId = userId, TenantId = tenantId };

        // Act
        GetCurrentUserResponse response = await BuildHandler().Handle(query, CancellationToken.None);

        // Assert
        response.DisplayName.Should().BeNull();
        response.TenantName.Should().Be("My Tenant");
    }

    [Fact]
    public async Task Handle_UserNotFound_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();

        _userRepo.Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        GetCurrentUserQuery query = new GetCurrentUserQuery { UserId = userId, TenantId = tenantId };

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => BuildHandler().Handle(query, CancellationToken.None));

        _tenantRepo.Verify(r => r.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_TenantNotFound_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();

        User user = new User
        {
            Id = userId,
            ExternalId = "user@example.com",
            IdentityProvider = "Local",
            Email = "user@example.com",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };

        _userRepo.Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _tenantRepo.Setup(r => r.FindByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        GetCurrentUserQuery query = new GetCurrentUserQuery { UserId = userId, TenantId = tenantId };

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => BuildHandler().Handle(query, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_CallsRepositoriesWithCorrectIds()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        Guid tenantMembershipId = Guid.NewGuid();

        _userRepo.Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = userId,
                ExternalId = "u",
                IdentityProvider = "Local",
                Email = "u@u.com",
                CreatedAt = DateTimeOffset.UtcNow,
                IsActive = true,
            });

        Tenant tenant = new Tenant
        {
            Id = tenantId,
            Name = "T",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };

        TenantMembership tenantMembership = new TenantMembership
        {
            Id = tenantMembershipId,
            TenantId = tenantId,
            Tenant = tenant,
            UserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };

        _tenantRepo.Setup(r => r.FindByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        _tenantMembRepo.Setup(r => r.GetActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantMembership>
            {
                tenantMembership
            });

        _tenantMembRepo.Setup(r => r.FindActiveByUserAndTenantAsync(userId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenantMembership);

        GetCurrentUserQuery query = new GetCurrentUserQuery { UserId = userId, TenantId = tenantId };

        // Act
        await BuildHandler().Handle(query, CancellationToken.None);

        // Assert
        _userRepo.Verify(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
        _tenantRepo.Verify(r => r.FindByIdAsync(tenantId, It.IsAny<CancellationToken>()), Times.Once);
        _tenantMembRepo.Verify(r => r.FindActiveByUserAndTenantAsync(userId, tenantId, It.IsAny<CancellationToken>()), Times.Once);
        _tenantMembRepo.Verify(r => r.GetActiveByUserAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
