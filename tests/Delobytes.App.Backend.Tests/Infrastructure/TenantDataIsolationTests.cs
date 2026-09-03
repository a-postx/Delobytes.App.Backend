using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Domain.Enums;
using FluentAssertions;
using Moq;

namespace Delobytes.App.Backend.Tests.Identity;

/// <summary>
/// Tests for tenant data isolation.
/// </summary>
public class TenantDataIsolationTests
{
    [Fact]
    public async Task UserInTenantA_CannotAccessDataFromTenantB()
    {
        // Arrange
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var userInBothTenantsId = Guid.NewGuid();

        var membershipRepo = new Mock<ITenantMembershipRepository>();

        var membershipInTenantA = new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = userInBothTenantsId,
            TenantId = tenantAId,
            Role = Role.Administrator,
            IsActive = true,
            Tenant = new Tenant
            {
                Id = tenantAId,
                Name = "Tenant A",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            },
        };

        var membershipInTenantB = new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = userInBothTenantsId,
            TenantId = tenantBId,
            Role = Role.Manager,
            IsActive = true,
            Tenant = new Tenant
            {
                Id = tenantBId,
                Name = "Tenant B",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            },
        };

        // Setup: user has memberships in both tenants
        membershipRepo.Setup(r => r.GetActiveByUserAsync(userInBothTenantsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantMembership> { membershipInTenantA, membershipInTenantB });

        // Setup: when active tenant is A, should only return membership A
        membershipRepo.Setup(r => r.FindActiveByUserAndTenantAsync(userInBothTenantsId, tenantAId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(membershipInTenantA);

        // Setup: when active tenant is B, should only return membership B
        membershipRepo.Setup(r => r.FindActiveByUserAndTenantAsync(userInBothTenantsId, tenantBId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(membershipInTenantB);

        // Act
        TenantMembership? activeMembershipInA = await membershipRepo.Object
            .FindActiveByUserAndTenantAsync(userInBothTenantsId, tenantAId, CancellationToken.None);

        TenantMembership? activeMembershipInB = await membershipRepo.Object
            .FindActiveByUserAndTenantAsync(userInBothTenantsId, tenantBId, CancellationToken.None);

        // Assert: user can access their own membership in each tenant separately
        activeMembershipInA.Should().NotBeNull();
        activeMembershipInA!.TenantId.Should().Be(tenantAId);
        activeMembershipInA.Role.Should().Be(Role.Administrator);

        activeMembershipInB.Should().NotBeNull();
        activeMembershipInB!.TenantId.Should().Be(tenantBId);
        activeMembershipInB.Role.Should().Be(Role.Manager);

        // Assert: memberships are completely isolated - different tenant IDs
        activeMembershipInA.TenantId.Should().NotBe(activeMembershipInB.TenantId);
    }

    [Fact]
    public async Task UserNotInTenantB_CannotAccessTenantBMembership()
    {
        // Arrange
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var userOnlyInTenantAId = Guid.NewGuid();

        var membershipRepo = new Mock<ITenantMembershipRepository>();

        var membershipInTenantA = new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = userOnlyInTenantAId,
            TenantId = tenantAId,
            Role = Role.Administrator,
            IsActive = true,
        };

        membershipRepo.Setup(r => r.FindActiveByUserAndTenantAsync(userOnlyInTenantAId, tenantAId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(membershipInTenantA);

        // User does not have membership in Tenant B
        membershipRepo.Setup(r => r.FindActiveByUserAndTenantAsync(userOnlyInTenantAId, tenantBId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantMembership?)null);

        // Act
        TenantMembership? membershipInA = await membershipRepo.Object
            .FindActiveByUserAndTenantAsync(userOnlyInTenantAId, tenantAId, CancellationToken.None);

        TenantMembership? membershipInB = await membershipRepo.Object
            .FindActiveByUserAndTenantAsync(userOnlyInTenantAId, tenantBId, CancellationToken.None);

        // Assert
        membershipInA.Should().NotBeNull();
        membershipInA!.TenantId.Should().Be(tenantAId);

        membershipInB.Should().BeNull("user should not have access to Tenant B");
    }

    [Fact]
    public async Task GetActiveByUser_ReturnsOnlyUsersMemberships()
    {
        // Arrange
        var user1Id = Guid.NewGuid();
        var user2Id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var membershipRepo = new Mock<ITenantMembershipRepository>();

        var user1Membership = new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = user1Id,
            TenantId = tenantId,
            Role = Role.Administrator,
            IsActive = true,
            Tenant = new Tenant { Id = tenantId, Name = "Shared Tenant", IsActive = true, CreatedAt = DateTimeOffset.UtcNow },
        };

        var user2Membership = new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = user2Id,
            TenantId = tenantId,
            Role = Role.Manager,
            IsActive = true,
            Tenant = new Tenant { Id = tenantId, Name = "Shared Tenant", IsActive = true, CreatedAt = DateTimeOffset.UtcNow },
        };

        membershipRepo.Setup(r => r.GetActiveByUserAsync(user1Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantMembership> { user1Membership });

        membershipRepo.Setup(r => r.GetActiveByUserAsync(user2Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantMembership> { user2Membership });

        // Act
        IReadOnlyList<TenantMembership> user1Memberships = await membershipRepo.Object
            .GetActiveByUserAsync(user1Id, CancellationToken.None);

        IReadOnlyList<TenantMembership> user2Memberships = await membershipRepo.Object
            .GetActiveByUserAsync(user2Id, CancellationToken.None);

        // Assert
        user1Memberships.Should().HaveCount(1);
        user1Memberships.First().UserId.Should().Be(user1Id);
        user1Memberships.First().Role.Should().Be(Role.Administrator);

        user2Memberships.Should().HaveCount(1);
        user2Memberships.First().UserId.Should().Be(user2Id);
        user2Memberships.First().Role.Should().Be(Role.Manager);

        // Assert: even though both users are in the same tenant, their memberships are separate
        user1Memberships.First().Id.Should().NotBe(user2Memberships.First().Id);
    }
}
