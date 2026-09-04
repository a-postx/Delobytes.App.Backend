using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Identity.Application.Commands.RevokeInvitation;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity;

/// <summary>
/// Unit tests for RevokeInvitationCommandHandler.
/// </summary>
public class RevokeInvitationCommandHandlerTests
{
    private readonly Mock<IInvitationRepository> _invitationRepo;
    private readonly Mock<ITenantMembershipRepository> _membershipRepo;

    public RevokeInvitationCommandHandlerTests()
    {
        _invitationRepo = new Mock<IInvitationRepository>();
        _membershipRepo = new Mock<ITenantMembershipRepository>();
    }

    private RevokeInvitationCommandHandler BuildHandler()
        => new RevokeInvitationCommandHandler(
            _invitationRepo.Object,
            _membershipRepo.Object);

    private static RevokeInvitationCommand BuildCommand(Guid invitationId, Guid tenantId, Guid revokedByUserId)
        => new RevokeInvitationCommand
        {
            InvitationId = invitationId,
            TenantId = tenantId,
            RevokedByUserId = revokedByUserId,
        };

    private static Invitation BuildInvitation(Guid invitationId, Guid tenantId, string email = "user@example.com")
        => new Invitation
        {
            Id = invitationId,
            TenantId = tenantId,
            Email = email,
            Role = Role.Manager,
            Token = "token-" + invitationId.ToString(),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            IsAccepted = false,
        };

    private static TenantMembership BuildMembership(Guid userId, Guid tenantId, Role role = Role.Administrator)
        => new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = tenantId,
            Role = role,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    // ── Authorization checks ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_RevokerNotMemberOfTenant_ThrowsInvalidOperationException()
    {
        // Arrange
        Guid invitationId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        Guid revokerId = Guid.NewGuid();

        RevokeInvitationCommand command = BuildCommand(invitationId, tenantId, revokerId);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(revokerId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantMembership?)null);

        // Act & Assert
        Func<Task> act = async () => await BuildHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*администратор*");
    }

    [Fact]
    public async Task Handle_RevokerIsNotAdministrator_ThrowsInvalidOperationException()
    {
        // Arrange
        Guid invitationId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        Guid revokerId = Guid.NewGuid();

        RevokeInvitationCommand command = BuildCommand(invitationId, tenantId, revokerId);

        TenantMembership revokerMembership = BuildMembership(revokerId, tenantId, Role.Manager);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(revokerId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(revokerMembership);

        // Act & Assert
        Func<Task> act = async () => await BuildHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*администратор*");
    }

    [Fact]
    public async Task Handle_RevokerIsReadOnly_ThrowsInvalidOperationException()
    {
        // Arrange
        Guid invitationId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        Guid revokerId = Guid.NewGuid();

        RevokeInvitationCommand command = BuildCommand(invitationId, tenantId, revokerId);

        TenantMembership revokerMembership = BuildMembership(revokerId, tenantId, Role.ReadOnly);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(revokerId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(revokerMembership);

        // Act & Assert
        Func<Task> act = async () => await BuildHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*администратор*");
    }

    // ── Tenant isolation checks ───────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_InvitationBelongsToDifferentTenant_ThrowsInvalidOperationException()
    {
        // Arrange
        Guid invitationId = Guid.NewGuid();
        Guid tenant1Id = Guid.NewGuid();
        Guid tenant2Id = Guid.NewGuid();
        Guid revokerId = Guid.NewGuid();

        // User is admin of tenant1, but invitation belongs to tenant2
        RevokeInvitationCommand command = BuildCommand(invitationId, tenant1Id, revokerId);

        TenantMembership revokerMembership = BuildMembership(revokerId, tenant1Id, Role.Administrator);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(revokerId, tenant1Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(revokerMembership);

        Invitation invitation = BuildInvitation(invitationId, tenant2Id);

        _invitationRepo
            .Setup(r => r.FindByIdAsync(invitationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        // Act & Assert
        Func<Task> act = async () => await BuildHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*не принадлежит*");

        _invitationRepo.Verify(r => r.Remove(It.IsAny<Invitation>()), Times.Never);
        _invitationRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AdminFromDifferentTenantCannotRevokeInvitation()
    {
        // Arrange
        Guid invitationId = Guid.NewGuid();
        Guid tenant1Id = Guid.NewGuid();
        Guid tenant2Id = Guid.NewGuid();
        Guid admin1Id = Guid.NewGuid();

        // Admin of tenant1 tries to revoke invitation for tenant2
        RevokeInvitationCommand command = BuildCommand(invitationId, tenant2Id, admin1Id);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(admin1Id, tenant2Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantMembership?)null);

        // Act & Assert
        Func<Task> act = async () => await BuildHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();

        _invitationRepo.Verify(r => r.Remove(It.IsAny<Invitation>()), Times.Never);
    }

    // ── Validation checks ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_InvitationNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        Guid invitationId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        Guid revokerId = Guid.NewGuid();

        RevokeInvitationCommand command = BuildCommand(invitationId, tenantId, revokerId);

        TenantMembership revokerMembership = BuildMembership(revokerId, tenantId, Role.Administrator);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(revokerId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(revokerMembership);

        _invitationRepo
            .Setup(r => r.FindByIdAsync(invitationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Invitation?)null);

        // Act & Assert
        Func<Task> act = async () => await BuildHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*не найдено*");
    }

    [Fact]
    public async Task Handle_InvitationAlreadyAccepted_ThrowsInvalidOperationException()
    {
        // Arrange
        Guid invitationId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        Guid revokerId = Guid.NewGuid();

        RevokeInvitationCommand command = BuildCommand(invitationId, tenantId, revokerId);

        TenantMembership revokerMembership = BuildMembership(revokerId, tenantId, Role.Administrator);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(revokerId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(revokerMembership);

        Invitation invitation = BuildInvitation(invitationId, tenantId);
        invitation.IsAccepted = true;
        invitation.AcceptedAt = DateTimeOffset.UtcNow.AddDays(-1);
        invitation.AcceptedByUserId = Guid.NewGuid();

        _invitationRepo
            .Setup(r => r.FindByIdAsync(invitationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        // Act & Assert
        Func<Task> act = async () => await BuildHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*принятое приглашение*");
    }

    // ── Successful revocation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ValidRequest_RemovesInvitation()
    {
        // Arrange
        Guid invitationId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        Guid revokerId = Guid.NewGuid();

        RevokeInvitationCommand command = BuildCommand(invitationId, tenantId, revokerId);

        TenantMembership revokerMembership = BuildMembership(revokerId, tenantId, Role.Administrator);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(revokerId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(revokerMembership);

        Invitation invitation = BuildInvitation(invitationId, tenantId);

        _invitationRepo
            .Setup(r => r.FindByIdAsync(invitationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        _invitationRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        RevokeInvitationResponse response = await BuildHandler().Handle(command, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();

        _invitationRepo.Verify(r => r.Remove(invitation), Times.Once);
        _invitationRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidRequest_AdministratorCanRevokeExpiredInvitation()
    {
        // Arrange
        Guid invitationId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        Guid revokerId = Guid.NewGuid();

        RevokeInvitationCommand command = BuildCommand(invitationId, tenantId, revokerId);

        TenantMembership revokerMembership = BuildMembership(revokerId, tenantId, Role.Administrator);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(revokerId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(revokerMembership);

        Invitation invitation = BuildInvitation(invitationId, tenantId);
        invitation.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);

        _invitationRepo
            .Setup(r => r.FindByIdAsync(invitationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        _invitationRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // Act
        RevokeInvitationResponse response = await BuildHandler().Handle(command, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();

        _invitationRepo.Verify(r => r.Remove(invitation), Times.Once);
    }
}
