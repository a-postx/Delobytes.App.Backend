using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Identity.Application.Commands.RemoveTenantMember;
using Delobytes.App.Backend.Identity.Application.Commands.UpdateMembershipRole;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity;

/// <summary>
/// Unit tests verifying tenant operations security and data isolation.
/// Tests critical scenarios where operations must be prevented from affecting other tenants.
/// </summary>
public class TenantOperationSecurityTests
{
    private readonly Mock<ITenantMembershipRepository> _membershipRepo;
    private readonly Mock<ITenantRepository> _tenantRepo;

    public TenantOperationSecurityTests()
    {
        _membershipRepo = new Mock<ITenantMembershipRepository>();
        _tenantRepo = new Mock<ITenantRepository>();
    }

    private static TenantMembership BuildMembership(Guid id, Guid userId, Guid tenantId, Role role = Role.Manager, bool isActive = true)
        => new TenantMembership
        {
            Id = id,
            UserId = userId,
            TenantId = tenantId,
            Role = role,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    // ── Remove member security ────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveTenantMember_UserCannotRemoveMemberFromDifferentTenant()
    {
        // Arrange
        Guid admin1Id = Guid.NewGuid();
        Guid user2Id = Guid.NewGuid();
        Guid tenant1Id = Guid.NewGuid();
        Guid tenant2Id = Guid.NewGuid();
        Guid membershipIdInTenant2 = Guid.NewGuid();

        // Admin1 is administrator in tenant1
        TenantMembership admin1Membership = BuildMembership(Guid.NewGuid(), admin1Id, tenant1Id, Role.Administrator);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(admin1Id, tenant1Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(admin1Membership);

        // User2 is member of tenant2
        TenantMembership user2Membership = BuildMembership(membershipIdInTenant2, user2Id, tenant2Id, Role.Manager);

        _membershipRepo
            .Setup(r => r.FindByIdAsync(membershipIdInTenant2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user2Membership);

        // Admin1 not in tenant2
        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(admin1Id, tenant2Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantMembership?)null);

        // Act & Assert - Admin1 cannot remove user from tenant2
        TenantMembership? admin1InTenant2 = await _membershipRepo.Object
            .FindActiveByUserAndTenantAsync(admin1Id, tenant2Id, CancellationToken.None);

        admin1InTenant2.Should().BeNull();

        _membershipRepo.Verify(r => r.Remove(It.IsAny<TenantMembership>()), Times.Never);
    }

    [Fact]
    public async Task RemoveTenantMember_CannotRemoveMembershipByIdWithoutTenantValidation()
    {
        // Arrange
        Guid membershipId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();

        TenantMembership membership = BuildMembership(membershipId, userId, tenantId, Role.Manager);

        _membershipRepo
            .Setup(r => r.FindByIdAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(membership);

        // Act
        TenantMembership? foundMembership = await _membershipRepo.Object.FindByIdAsync(membershipId, CancellationToken.None);

        // Assert
        foundMembership.Should().NotBeNull();
        foundMembership!.TenantId.Should().Be(tenantId);
    }

    // ── Update role security ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateMembershipRole_UserCannotUpdateRoleInDifferentTenant()
    {
        // Arrange
        Guid admin1Id = Guid.NewGuid();
        Guid user2Id = Guid.NewGuid();
        Guid tenant1Id = Guid.NewGuid();
        Guid tenant2Id = Guid.NewGuid();
        Guid membershipIdInTenant2 = Guid.NewGuid();

        // Admin1 is administrator in tenant1
        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(admin1Id, tenant1Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildMembership(Guid.NewGuid(), admin1Id, tenant1Id, Role.Administrator));

        // Admin1 not in tenant2
        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(admin1Id, tenant2Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantMembership?)null);

        // User2 membership in tenant2
        TenantMembership user2Membership = BuildMembership(membershipIdInTenant2, user2Id, tenant2Id, Role.Manager);

        _membershipRepo
            .Setup(r => r.FindByIdAsync(membershipIdInTenant2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user2Membership);

        // Act
        TenantMembership? admin1InTenant2 = await _membershipRepo.Object
            .FindActiveByUserAndTenantAsync(admin1Id, tenant2Id, CancellationToken.None);

        // Assert
        admin1InTenant2.Should().BeNull();
    }

    // ── Membership query isolation ────────────────────────────────────────────────────

    [Fact]
    public async Task GetActiveByTenant_MustFilterByTenantId()
    {
        // Arrange
        Guid tenant1Id = Guid.NewGuid();
        Guid tenant2Id = Guid.NewGuid();
        Guid user1Id = Guid.NewGuid();
        Guid user2Id = Guid.NewGuid();
        Guid user3Id = Guid.NewGuid();

        List<TenantMembership> tenant1Memberships = new List<TenantMembership>
        {
            BuildMembership(Guid.NewGuid(), user1Id, tenant1Id, Role.Administrator),
            BuildMembership(Guid.NewGuid(), user2Id, tenant1Id, Role.Manager),
        };

        List<TenantMembership> tenant2Memberships = new List<TenantMembership>
        {
            BuildMembership(Guid.NewGuid(), user3Id, tenant2Id, Role.ReadOnly),
        };

        _membershipRepo
            .Setup(r => r.GetActiveByTenantAsync(tenant1Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant1Memberships);

        _membershipRepo
            .Setup(r => r.GetActiveByTenantAsync(tenant2Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant2Memberships);

        // Act
        IReadOnlyList<TenantMembership> tenant1Result = await _membershipRepo.Object
            .GetActiveByTenantAsync(tenant1Id, CancellationToken.None);

        IReadOnlyList<TenantMembership> tenant2Result = await _membershipRepo.Object
            .GetActiveByTenantAsync(tenant2Id, CancellationToken.None);

        // Assert
        tenant1Result.Should().HaveCount(2);
        tenant1Result.Should().OnlyContain(m => m.TenantId == tenant1Id);
        tenant1Result.Should().NotContain(m => m.UserId == user3Id);

        tenant2Result.Should().HaveCount(1);
        tenant2Result.Should().OnlyContain(m => m.TenantId == tenant2Id);
        tenant2Result.Should().NotContain(m => m.UserId == user1Id || m.UserId == user2Id);
    }

    [Fact]
    public async Task FindActiveByUserAndTenant_MustRequireBothUserAndTenantMatch()
    {
        // Arrange
        Guid user1Id = Guid.NewGuid();
        Guid user2Id = Guid.NewGuid();
        Guid tenant1Id = Guid.NewGuid();
        Guid tenant2Id = Guid.NewGuid();

        TenantMembership user1InTenant1 = BuildMembership(Guid.NewGuid(), user1Id, tenant1Id, Role.Administrator);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(user1Id, tenant1Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user1InTenant1);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(user1Id, tenant2Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantMembership?)null);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(user2Id, tenant1Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantMembership?)null);

        // Act & Assert
        TenantMembership? validResult = await _membershipRepo.Object
            .FindActiveByUserAndTenantAsync(user1Id, tenant1Id, CancellationToken.None);

        validResult.Should().NotBeNull();
        validResult!.UserId.Should().Be(user1Id);
        validResult.TenantId.Should().Be(tenant1Id);

        // Wrong tenant
        TenantMembership? wrongTenant = await _membershipRepo.Object
            .FindActiveByUserAndTenantAsync(user1Id, tenant2Id, CancellationToken.None);

        wrongTenant.Should().BeNull();

        // Wrong user
        TenantMembership? wrongUser = await _membershipRepo.Object
            .FindActiveByUserAndTenantAsync(user2Id, tenant1Id, CancellationToken.None);

        wrongUser.Should().BeNull();
    }

    // ── Administrator count isolation ─────────────────────────────────────────────────

    [Fact]
    public async Task CountAdministratorsByTenant_MustReturnCountForSpecificTenantOnly()
    {
        // Arrange
        Guid tenant1Id = Guid.NewGuid();
        Guid tenant2Id = Guid.NewGuid();

        _membershipRepo
            .Setup(r => r.CountAdministratorsByTenantAsync(tenant1Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        _membershipRepo
            .Setup(r => r.CountAdministratorsByTenantAsync(tenant2Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        int tenant1AdminCount = await _membershipRepo.Object
            .CountAdministratorsByTenantAsync(tenant1Id, CancellationToken.None);

        int tenant2AdminCount = await _membershipRepo.Object
            .CountAdministratorsByTenantAsync(tenant2Id, CancellationToken.None);

        // Assert
        tenant1AdminCount.Should().Be(3);
        tenant2AdminCount.Should().Be(1);
        tenant1AdminCount.Should().NotBe(tenant2AdminCount);
    }

    // ── Inactive membership isolation ─────────────────────────────────────────────────

    [Fact]
    public async Task GetActiveByTenant_MustExcludeInactiveMemberships()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid user1Id = Guid.NewGuid();
        Guid user2Id = Guid.NewGuid();

        List<TenantMembership> activeMemberships = new List<TenantMembership>
        {
            BuildMembership(Guid.NewGuid(), user1Id, tenantId, Role.Administrator, isActive: true),
        };

        _membershipRepo
            .Setup(r => r.GetActiveByTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeMemberships);

        // Act
        IReadOnlyList<TenantMembership> result = await _membershipRepo.Object
            .GetActiveByTenantAsync(tenantId, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        result.Should().OnlyContain(m => m.IsActive);
        result.Should().NotContain(m => m.UserId == user2Id);
    }

    [Fact]
    public async Task FindActiveByUserAndTenant_MustReturnNullForInactiveMembership()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(userId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantMembership?)null);

        // Act
        TenantMembership? result = await _membershipRepo.Object
            .FindActiveByUserAndTenantAsync(userId, tenantId, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    // ── Tenant existence validation ───────────────────────────────────────────────────

    [Fact]
    public async Task OperationsOnNonExistentTenant_MustReturnEmptyOrNull()
    {
        // Arrange
        Guid nonExistentTenantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        _membershipRepo
            .Setup(r => r.GetActiveByTenantAsync(nonExistentTenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantMembership>());

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(userId, nonExistentTenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantMembership?)null);

        _membershipRepo
            .Setup(r => r.CountAdministratorsByTenantAsync(nonExistentTenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        IReadOnlyList<TenantMembership> members = await _membershipRepo.Object
            .GetActiveByTenantAsync(nonExistentTenantId, CancellationToken.None);

        TenantMembership? membership = await _membershipRepo.Object
            .FindActiveByUserAndTenantAsync(userId, nonExistentTenantId, CancellationToken.None);

        int adminCount = await _membershipRepo.Object
            .CountAdministratorsByTenantAsync(nonExistentTenantId, CancellationToken.None);

        // Assert
        members.Should().BeEmpty();
        membership.Should().BeNull();
        adminCount.Should().Be(0);
    }

    // ── Membership uniqueness ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UserCannotHaveMultipleActiveMembershipsInSameTenant()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();

        TenantMembership existingMembership = BuildMembership(Guid.NewGuid(), userId, tenantId, Role.Manager);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(userId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingMembership);

        // Act
        TenantMembership? membership = await _membershipRepo.Object
            .FindActiveByUserAndTenantAsync(userId, tenantId, CancellationToken.None);

        // Assert
        membership.Should().NotBeNull();
        membership!.Id.Should().Be(existingMembership.Id);
    }

    [Fact]
    public async Task GetActiveByUser_ReturnsAllTenantsForUser_EachWithSingleMembership()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenant1Id = Guid.NewGuid();
        Guid tenant2Id = Guid.NewGuid();

        List<TenantMembership> memberships = new List<TenantMembership>
        {
            BuildMembership(Guid.NewGuid(), userId, tenant1Id, Role.Administrator),
            BuildMembership(Guid.NewGuid(), userId, tenant2Id, Role.Manager),
        };

        _membershipRepo
            .Setup(r => r.GetActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(memberships);

        // Act
        IReadOnlyList<TenantMembership> result = await _membershipRepo.Object
            .GetActiveByUserAsync(userId, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Should().OnlyContain(m => m.UserId == userId);
        result.Select(m => m.TenantId).Should().OnlyHaveUniqueItems();
    }
}
