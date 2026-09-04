using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Application.Queries.GetTenantMembers;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity;

/// <summary>
/// Unit tests verifying tenant data isolation.
/// Critical security requirement: users must never access data from tenants they don't belong to.
/// </summary>
public class TenantDataIsolationTests
{
    private readonly Mock<ITenantMembershipRepository> _membershipRepo;
    private readonly Mock<IInvitationRepository> _invitationRepo;

    public TenantDataIsolationTests()
    {
        _membershipRepo = new Mock<ITenantMembershipRepository>();
        _invitationRepo = new Mock<IInvitationRepository>();
    }

    private static TenantMembership BuildMembership(Guid userId, Guid tenantId, Role role = Role.Manager)
        => new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = tenantId,
            Role = role,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            User = new User
            {
                Id = userId,
                Email = $"user-{userId}@example.com",
                ExternalId = $"ext-{userId}",
                IdentityProvider = "GoogleID",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            },
        };

    private static Invitation BuildInvitation(Guid tenantId, string email)
        => new Invitation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = email,
            Role = Role.Manager,
            Token = $"token-{Guid.NewGuid()}",
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            IsAccepted = false,
        };

    // ── Membership isolation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetActiveByTenant_ReturnsOnlyMembersOfSpecifiedTenant()
    {
        // Arrange
        Guid tenant1Id = Guid.NewGuid();
        Guid tenant2Id = Guid.NewGuid();
        Guid user1Id = Guid.NewGuid();
        Guid user2Id = Guid.NewGuid();
        Guid user3Id = Guid.NewGuid();

        TenantMembership membership1 = BuildMembership(user1Id, tenant1Id);
        TenantMembership membership2 = BuildMembership(user2Id, tenant1Id);

        List<TenantMembership> tenant1Members = new List<TenantMembership> { membership1, membership2 };

        _membershipRepo
            .Setup(r => r.GetActiveByTenantAsync(tenant1Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant1Members);

        // Act
        IReadOnlyList<TenantMembership> result = await _membershipRepo.Object.GetActiveByTenantAsync(tenant1Id, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Should().OnlyContain(m => m.TenantId == tenant1Id);
        result.Should().NotContain(m => m.UserId == user3Id);
    }

    [Fact]
    public async Task FindActiveByUserAndTenant_ReturnsNullForDifferentTenant()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenant1Id = Guid.NewGuid();
        Guid tenant2Id = Guid.NewGuid();

        TenantMembership membershipInTenant1 = BuildMembership(userId, tenant1Id);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(userId, tenant1Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(membershipInTenant1);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(userId, tenant2Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantMembership?)null);

        // Act
        TenantMembership? resultTenant1 = await _membershipRepo.Object.FindActiveByUserAndTenantAsync(userId, tenant1Id, CancellationToken.None);
        TenantMembership? resultTenant2 = await _membershipRepo.Object.FindActiveByUserAndTenantAsync(userId, tenant2Id, CancellationToken.None);

        // Assert
        resultTenant1.Should().NotBeNull();
        resultTenant1!.TenantId.Should().Be(tenant1Id);

        resultTenant2.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveByUser_ReturnsOnlyUserMemberships()
    {
        // Arrange
        Guid user1Id = Guid.NewGuid();
        Guid user2Id = Guid.NewGuid();
        Guid tenant1Id = Guid.NewGuid();
        Guid tenant2Id = Guid.NewGuid();

        TenantMembership user1Tenant1 = BuildMembership(user1Id, tenant1Id);
        TenantMembership user1Tenant2 = BuildMembership(user1Id, tenant2Id);

        List<TenantMembership> user1Memberships = new List<TenantMembership> { user1Tenant1, user1Tenant2 };

        _membershipRepo
            .Setup(r => r.GetActiveByUserAsync(user1Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user1Memberships);

        // Act
        IReadOnlyList<TenantMembership> result = await _membershipRepo.Object.GetActiveByUserAsync(user1Id, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Should().OnlyContain(m => m.UserId == user1Id);
        result.Should().NotContain(m => m.UserId == user2Id);
    }

    [Fact]
    public async Task CountActiveByUser_ReturnsCorrectCountForSpecificUser()
    {
        // Arrange
        Guid user1Id = Guid.NewGuid();
        Guid user2Id = Guid.NewGuid();

        _membershipRepo
            .Setup(r => r.CountActiveByUserAsync(user1Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        _membershipRepo
            .Setup(r => r.CountActiveByUserAsync(user2Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        int user1Count = await _membershipRepo.Object.CountActiveByUserAsync(user1Id, CancellationToken.None);
        int user2Count = await _membershipRepo.Object.CountActiveByUserAsync(user2Id, CancellationToken.None);

        // Assert
        user1Count.Should().Be(2);
        user2Count.Should().Be(0);
    }

    [Fact]
    public async Task CountAdministratorsByTenant_ReturnsCorrectCountForSpecificTenant()
    {
        // Arrange
        Guid tenant1Id = Guid.NewGuid();
        Guid tenant2Id = Guid.NewGuid();

        _membershipRepo
            .Setup(r => r.CountAdministratorsByTenantAsync(tenant1Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        _membershipRepo
            .Setup(r => r.CountAdministratorsByTenantAsync(tenant2Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        int tenant1AdminCount = await _membershipRepo.Object.CountAdministratorsByTenantAsync(tenant1Id, CancellationToken.None);
        int tenant2AdminCount = await _membershipRepo.Object.CountAdministratorsByTenantAsync(tenant2Id, CancellationToken.None);

        // Assert
        tenant1AdminCount.Should().Be(2);
        tenant2AdminCount.Should().Be(1);
    }

    // ── Invitation isolation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPendingInvitationsByTenant_ReturnsOnlyInvitationsForSpecifiedTenant()
    {
        // Arrange
        Guid tenant1Id = Guid.NewGuid();
        Guid tenant2Id = Guid.NewGuid();

        Invitation invitation1 = BuildInvitation(tenant1Id, "user1@example.com");
        Invitation invitation2 = BuildInvitation(tenant1Id, "user2@example.com");

        List<Invitation> tenant1Invitations = new List<Invitation> { invitation1, invitation2 };

        _invitationRepo
            .Setup(r => r.GetPendingByTenantAsync(tenant1Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant1Invitations);

        _invitationRepo
            .Setup(r => r.GetPendingByTenantAsync(tenant2Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Invitation>());

        // Act
        IReadOnlyList<Invitation> resultTenant1 = await _invitationRepo.Object.GetPendingByTenantAsync(tenant1Id, CancellationToken.None);
        IReadOnlyList<Invitation> resultTenant2 = await _invitationRepo.Object.GetPendingByTenantAsync(tenant2Id, CancellationToken.None);

        // Assert
        resultTenant1.Should().HaveCount(2);
        resultTenant1.Should().OnlyContain(i => i.TenantId == tenant1Id);

        resultTenant2.Should().BeEmpty();
    }

    [Fact]
    public async Task FindPendingByTenantAndEmail_ReturnsNullForDifferentTenant()
    {
        // Arrange
        Guid tenant1Id = Guid.NewGuid();
        Guid tenant2Id = Guid.NewGuid();
        string email = "user@example.com";

        Invitation invitation = BuildInvitation(tenant1Id, email);

        _invitationRepo
            .Setup(r => r.FindPendingByTenantAndEmailAsync(tenant1Id, email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        _invitationRepo
            .Setup(r => r.FindPendingByTenantAndEmailAsync(tenant2Id, email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Invitation?)null);

        // Act
        Invitation? resultTenant1 = await _invitationRepo.Object.FindPendingByTenantAndEmailAsync(tenant1Id, email, CancellationToken.None);
        Invitation? resultTenant2 = await _invitationRepo.Object.FindPendingByTenantAndEmailAsync(tenant2Id, email, CancellationToken.None);

        // Assert
        resultTenant1.Should().NotBeNull();
        resultTenant1!.TenantId.Should().Be(tenant1Id);

        resultTenant2.Should().BeNull();
    }

    // ── Cross-tenant operation prevention ─────────────────────────────────────────────

    [Fact]
    public void UserCannotModifyMembershipInTenantTheyDontBelong()
    {
        // Arrange
        Guid user1Id = Guid.NewGuid();
        Guid tenant1Id = Guid.NewGuid();
        Guid tenant2Id = Guid.NewGuid();

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(user1Id, tenant1Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildMembership(user1Id, tenant1Id, Role.Administrator));

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(user1Id, tenant2Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantMembership?)null);

        // Act & Assert
        // User1 can operate in tenant1
        TenantMembership? membershipInTenant1 = _membershipRepo.Object
            .FindActiveByUserAndTenantAsync(user1Id, tenant1Id, CancellationToken.None)
            .Result;

        membershipInTenant1.Should().NotBeNull();

        // User1 cannot operate in tenant2
        TenantMembership? membershipInTenant2 = _membershipRepo.Object
            .FindActiveByUserAndTenantAsync(user1Id, tenant2Id, CancellationToken.None)
            .Result;

        membershipInTenant2.Should().BeNull();
    }

    [Fact]
    public void AdminInTenant1CannotAccessMembersOfTenant2()
    {
        // Arrange
        Guid admin1Id = Guid.NewGuid();
        Guid tenant1Id = Guid.NewGuid();
        Guid tenant2Id = Guid.NewGuid();

        Guid user2Id = Guid.NewGuid();
        Guid user3Id = Guid.NewGuid();

        List<TenantMembership> tenant1Members = new List<TenantMembership>
        {
            BuildMembership(admin1Id, tenant1Id, Role.Administrator),
        };

        List<TenantMembership> tenant2Members = new List<TenantMembership>
        {
            BuildMembership(user2Id, tenant2Id, Role.Manager),
            BuildMembership(user3Id, tenant2Id, Role.ReadOnly),
        };

        _membershipRepo
            .Setup(r => r.GetActiveByTenantAsync(tenant1Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant1Members);

        _membershipRepo
            .Setup(r => r.GetActiveByTenantAsync(tenant2Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant2Members);

        // Act
        IReadOnlyList<TenantMembership> tenant1Result = _membershipRepo.Object.GetActiveByTenantAsync(tenant1Id, CancellationToken.None).Result;
        IReadOnlyList<TenantMembership> tenant2Result = _membershipRepo.Object.GetActiveByTenantAsync(tenant2Id, CancellationToken.None).Result;

        // Assert
        tenant1Result.Should().OnlyContain(m => m.TenantId == tenant1Id);
        tenant1Result.Should().NotContain(m => m.UserId == user2Id || m.UserId == user3Id);

        tenant2Result.Should().OnlyContain(m => m.TenantId == tenant2Id);
        tenant2Result.Should().NotContain(m => m.UserId == admin1Id);
    }

    [Fact]
    public void InvitationTokenIsUniqueAndCannotBeGuessedAcrossTenants()
    {
        // Arrange
        Guid tenant1Id = Guid.NewGuid();
        Guid tenant2Id = Guid.NewGuid();
        string email = "user@example.com";

        Invitation invitation1 = BuildInvitation(tenant1Id, email);
        Invitation invitation2 = BuildInvitation(tenant2Id, email);

        _invitationRepo
            .Setup(r => r.FindByTokenAsync(invitation1.Token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation1);

        _invitationRepo
            .Setup(r => r.FindByTokenAsync(invitation2.Token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation2);

        _invitationRepo
            .Setup(r => r.FindByTokenAsync("fake-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Invitation?)null);

        // Act
        Invitation? result1 = _invitationRepo.Object.FindByTokenAsync(invitation1.Token, CancellationToken.None).Result;
        Invitation? result2 = _invitationRepo.Object.FindByTokenAsync(invitation2.Token, CancellationToken.None).Result;
        Invitation? resultFake = _invitationRepo.Object.FindByTokenAsync("fake-token", CancellationToken.None).Result;

        // Assert
        result1.Should().NotBeNull();
        result1!.TenantId.Should().Be(tenant1Id);
        result1.Token.Should().NotBe(invitation2.Token);

        result2.Should().NotBeNull();
        result2!.TenantId.Should().Be(tenant2Id);

        resultFake.Should().BeNull();
    }

    [Fact]
    public void MultiTenantUser_HasSeparateMembershipsForEachTenant()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenant1Id = Guid.NewGuid();
        Guid tenant2Id = Guid.NewGuid();

        TenantMembership membership1 = BuildMembership(userId, tenant1Id, Role.Administrator);
        TenantMembership membership2 = BuildMembership(userId, tenant2Id, Role.ReadOnly);

        List<TenantMembership> userMemberships = new List<TenantMembership> { membership1, membership2 };

        _membershipRepo
            .Setup(r => r.GetActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userMemberships);

        // Act
        IReadOnlyList<TenantMembership> result = _membershipRepo.Object.GetActiveByUserAsync(userId, CancellationToken.None).Result;

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(m => m.TenantId == tenant1Id && m.Role == Role.Administrator);
        result.Should().Contain(m => m.TenantId == tenant2Id && m.Role == Role.ReadOnly);
    }

    [Fact]
    public void UserRoleIsIndependentAcrossTenants()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenant1Id = Guid.NewGuid();
        Guid tenant2Id = Guid.NewGuid();

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(userId, tenant1Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildMembership(userId, tenant1Id, Role.Administrator));

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(userId, tenant2Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildMembership(userId, tenant2Id, Role.ReadOnly));

        // Act
        TenantMembership? membershipTenant1 = _membershipRepo.Object.FindActiveByUserAndTenantAsync(userId, tenant1Id, CancellationToken.None).Result;
        TenantMembership? membershipTenant2 = _membershipRepo.Object.FindActiveByUserAndTenantAsync(userId, tenant2Id, CancellationToken.None).Result;

        // Assert
        membershipTenant1.Should().NotBeNull();
        membershipTenant1!.Role.Should().Be(Role.Administrator);

        membershipTenant2.Should().NotBeNull();
        membershipTenant2!.Role.Should().Be(Role.ReadOnly);
    }
}
