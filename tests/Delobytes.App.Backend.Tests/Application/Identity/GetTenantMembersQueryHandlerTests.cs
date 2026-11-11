using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Application.Queries;
using Delobytes.App.Backend.Identity.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity;

public class GetTenantMembersQueryHandlerTests
{
    private readonly Mock<ITenantMembershipRepository> _membershipRepository;
    private readonly Mock<IInvitationRepository> _invitationRepository;
    private readonly GetTenantMembersQueryHandler _handler;

    public GetTenantMembersQueryHandlerTests()
    {
        _membershipRepository = new Mock<ITenantMembershipRepository>();
        _invitationRepository = new Mock<IInvitationRepository>();
        _handler = new GetTenantMembersQueryHandler(_membershipRepository.Object, _invitationRepository.Object);
    }

    private static GetTenantMembersQuery BuildQuery(Guid tenantId, Guid requestedByUserId)
    {
        return new GetTenantMembersQuery
        {
            TenantId = tenantId,
            RequestedByUserId = requestedByUserId,
        };
    }

    private static TenantMembership BuildMembership(Guid tenantId, User user, Role role, DateTimeOffset joinedAt)
    {
        return new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TenantId = tenantId,
            Role = role,
            IsActive = true,
            CreatedAt = joinedAt,
            User = user,
        };
    }

    [Fact]
    public async Task Handle_WhenRequesterIsNotTenantMember_ThrowsInvalidOperationException()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid requesterId = Guid.NewGuid();

        _membershipRepository
            .Setup(r => r.FindActiveByUserAndTenantAsync(requesterId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantMembership?)null);

        // Act
        Func<Task> act = async () => await _handler.Handle(BuildQuery(tenantId, requesterId), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Вы не являетесь членом данного тенанта.");

        _membershipRepository.Verify(
            r => r.GetActiveByTenantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _invitationRepository.Verify(
            r => r.GetPendingByTenantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenTenantHasNoMembersOrInvitations_ReturnsEmptyCollections()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid requesterId = Guid.NewGuid();

        _membershipRepository
            .Setup(r => r.FindActiveByUserAndTenantAsync(requesterId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildMembership(tenantId, new User { Id = requesterId, Email = "admin@example.com" }, Role.Administrator, DateTimeOffset.UtcNow));
        _membershipRepository
            .Setup(r => r.GetActiveByTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TenantMembership>());
        _invitationRepository
            .Setup(r => r.GetPendingByTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Invitation>());

        // Act
        GetTenantMembersResponse response = await _handler.Handle(BuildQuery(tenantId, requesterId), CancellationToken.None);

        // Assert
        response.Members.Should().BeEmpty();
        response.PendingInvitations.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenTenantHasMembersAndInvitations_MapsEveryField()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid requesterId = Guid.NewGuid();
        Guid memberUserId = Guid.NewGuid();
        DateTimeOffset joinedAt = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);

        User requester = new User { Id = requesterId, Email = "admin@example.com", DisplayName = "Админ" };
        User member = new User { Id = memberUserId, Email = "member@example.com", DisplayName = "Участник" };

        TenantMembership requesterMembership = BuildMembership(tenantId, requester, Role.Administrator, joinedAt);
        TenantMembership memberMembership = BuildMembership(tenantId, member, Role.Manager, joinedAt);

        Invitation invitation = new Invitation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = "invitee@example.com",
            Role = Role.ReadOnly,
            Token = "invitation-token",
            CreatedAt = joinedAt,
            CreatedByUserId = requesterId,
            ExpiresAt = joinedAt.AddDays(7),
        };

        _membershipRepository
            .Setup(r => r.FindActiveByUserAndTenantAsync(requesterId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(requesterMembership);
        _membershipRepository
            .Setup(r => r.GetActiveByTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { requesterMembership, memberMembership });
        _invitationRepository
            .Setup(r => r.GetPendingByTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { invitation });

        // Act
        GetTenantMembersResponse response = await _handler.Handle(BuildQuery(tenantId, requesterId), CancellationToken.None);

        // Assert
        response.Members.Should().HaveCount(2);

        TenantMemberInfo memberInfo = response.Members.Single(m => m.UserId == memberUserId);
        memberInfo.MembershipId.Should().Be(memberMembership.Id);
        memberInfo.Email.Should().Be("member@example.com");
        memberInfo.DisplayName.Should().Be("Участник");
        memberInfo.Role.Should().Be("Manager");
        memberInfo.JoinedAt.Should().Be(joinedAt);

        PendingInvitationInfo invitationInfo = response.PendingInvitations.Should().ContainSingle().Subject;
        invitationInfo.InvitationId.Should().Be(invitation.Id);
        invitationInfo.Email.Should().Be("invitee@example.com");
        invitationInfo.Role.Should().Be("ReadOnly");
        invitationInfo.Token.Should().Be("invitation-token");
        invitationInfo.CreatedAt.Should().Be(joinedAt);
        invitationInfo.CreatedByUserId.Should().Be(requesterId);
        invitationInfo.ExpiresAt.Should().Be(joinedAt.AddDays(7));
    }

    [Fact]
    public async Task Handle_WhenInvitationHasNoCreator_LeavesCreatedByUserIdNull()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid requesterId = Guid.NewGuid();

        Invitation invitation = new Invitation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = "legacy-invitee@example.com",
            Role = Role.Manager,
            Token = "legacy-token",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = null,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
        };

        _membershipRepository
            .Setup(r => r.FindActiveByUserAndTenantAsync(requesterId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildMembership(tenantId, new User { Id = requesterId, Email = "admin@example.com" }, Role.Administrator, DateTimeOffset.UtcNow));
        _membershipRepository
            .Setup(r => r.GetActiveByTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TenantMembership>());
        _invitationRepository
            .Setup(r => r.GetPendingByTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { invitation });

        // Act
        GetTenantMembersResponse response = await _handler.Handle(BuildQuery(tenantId, requesterId), CancellationToken.None);

        // Assert
        response.PendingInvitations.Should().ContainSingle();
        response.PendingInvitations[0].CreatedByUserId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenRequesterIsManager_ReturnsMembers()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid requesterId = Guid.NewGuid();
        User requester = new User { Id = requesterId, Email = "manager@example.com" };

        _membershipRepository
            .Setup(r => r.FindActiveByUserAndTenantAsync(requesterId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildMembership(tenantId, requester, Role.Manager, DateTimeOffset.UtcNow));
        _membershipRepository
            .Setup(r => r.GetActiveByTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { BuildMembership(tenantId, requester, Role.Manager, DateTimeOffset.UtcNow) });
        _invitationRepository
            .Setup(r => r.GetPendingByTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Invitation>());

        // Act
        GetTenantMembersResponse response = await _handler.Handle(BuildQuery(tenantId, requesterId), CancellationToken.None);

        // Assert
        response.Members.Should().ContainSingle(m => m.UserId == requesterId && m.Role == "Manager");
    }

    [Fact]
    public async Task Handle_QueriesBothRepositoriesForRequestedTenant()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid otherTenantId = Guid.NewGuid();
        Guid requesterId = Guid.NewGuid();
        User requester = new User { Id = requesterId, Email = "admin@example.com" };

        _membershipRepository
            .Setup(r => r.FindActiveByUserAndTenantAsync(requesterId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildMembership(tenantId, requester, Role.Administrator, DateTimeOffset.UtcNow));
        _membershipRepository
            .Setup(r => r.GetActiveByTenantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TenantMembership>());
        _invitationRepository
            .Setup(r => r.GetPendingByTenantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Invitation>());

        // Act
        await _handler.Handle(BuildQuery(tenantId, requesterId), CancellationToken.None);

        // Assert
        _membershipRepository.Verify(r => r.GetActiveByTenantAsync(tenantId, It.IsAny<CancellationToken>()), Times.Once);
        _invitationRepository.Verify(r => r.GetPendingByTenantAsync(tenantId, It.IsAny<CancellationToken>()), Times.Once);
        _membershipRepository.Verify(r => r.GetActiveByTenantAsync(otherTenantId, It.IsAny<CancellationToken>()), Times.Never);
        _invitationRepository.Verify(r => r.GetPendingByTenantAsync(otherTenantId, It.IsAny<CancellationToken>()), Times.Never);
    }
}
