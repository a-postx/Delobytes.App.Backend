using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Identity.Application.Commands;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity;

public class UpdateMembershipRoleCommandHandlerTests
{
    private readonly Mock<ITenantMembershipRepository> _membershipRepository;
    private readonly UpdateMembershipRoleCommandHandler _handler;

    public UpdateMembershipRoleCommandHandlerTests()
    {
        _membershipRepository = new Mock<ITenantMembershipRepository>();
        _handler = new UpdateMembershipRoleCommandHandler(_membershipRepository.Object);
    }

    private static TenantMembership BuildMembership(Guid userId, Guid tenantId, Role role)
    {
        return new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = tenantId,
            Role = role,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static UpdateMembershipRoleCommand BuildCommand(
        Guid tenantId,
        Guid targetUserId,
        Guid updatedByUserId,
        Role newRole)
    {
        return new UpdateMembershipRoleCommand
        {
            TenantId = tenantId,
            TargetUserId = targetUserId,
            UpdatedByUserId = updatedByUserId,
            NewRole = newRole,
        };
    }

    [Fact]
    public void AllowedRoles_IsAdministratorOnly()
    {
        // Act
        Role[] roles = new UpdateMembershipRoleCommand().AllowedRoles;

        // Assert
        roles.Should().BeEquivalentTo(new[] { Role.Administrator });
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
        Func<Task> act = async () => await _handler.Handle(
            BuildCommand(tenantId, Guid.NewGuid(), requesterId, Role.Manager),
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Только администратор может изменять роли пользователей.");

        _membershipRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenRequesterIsNotAdministrator_ThrowsInvalidOperationException()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid requesterId = Guid.NewGuid();

        _membershipRepository
            .Setup(r => r.FindActiveByUserAndTenantAsync(requesterId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildMembership(requesterId, tenantId, Role.ReadOnly));

        // Act
        Func<Task> act = async () => await _handler.Handle(
            BuildCommand(tenantId, Guid.NewGuid(), requesterId, Role.Manager),
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Только администратор может изменять роли пользователей.");

        _membershipRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenTargetIsNotTenantMember_ThrowsInvalidOperationException()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid requesterId = Guid.NewGuid();
        Guid targetUserId = Guid.NewGuid();

        _membershipRepository
            .Setup(r => r.FindActiveByUserAndTenantAsync(requesterId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildMembership(requesterId, tenantId, Role.Administrator));
        _membershipRepository
            .Setup(r => r.FindActiveByUserAndTenantAsync(targetUserId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantMembership?)null);

        // Act
        Func<Task> act = async () => await _handler.Handle(
            BuildCommand(tenantId, targetUserId, requesterId, Role.Manager),
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Пользователь не является членом данного тенанта.");
    }

    [Fact]
    public async Task Handle_WhenDemotingLastAdministrator_ThrowsInvalidOperationException()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid requesterId = Guid.NewGuid();
        Guid targetUserId = Guid.NewGuid();

        TenantMembership targetMembership = BuildMembership(targetUserId, tenantId, Role.Administrator);

        _membershipRepository
            .Setup(r => r.FindActiveByUserAndTenantAsync(requesterId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildMembership(requesterId, tenantId, Role.Administrator));
        _membershipRepository
            .Setup(r => r.FindActiveByUserAndTenantAsync(targetUserId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetMembership);
        _membershipRepository
            .Setup(r => r.CountAdministratorsByTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        Func<Task> act = async () => await _handler.Handle(
            BuildCommand(tenantId, targetUserId, requesterId, Role.Manager),
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Невозможно понизить роль последнего администратора тенанта.");

        targetMembership.Role.Should().Be(Role.Administrator);
        _membershipRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenDemotingAdministratorAndAnotherAdminExists_UpdatesRole()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid requesterId = Guid.NewGuid();
        Guid targetUserId = Guid.NewGuid();

        TenantMembership targetMembership = BuildMembership(targetUserId, tenantId, Role.Administrator);

        _membershipRepository
            .Setup(r => r.FindActiveByUserAndTenantAsync(requesterId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildMembership(requesterId, tenantId, Role.Administrator));
        _membershipRepository
            .Setup(r => r.FindActiveByUserAndTenantAsync(targetUserId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetMembership);
        _membershipRepository
            .Setup(r => r.CountAdministratorsByTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        _membershipRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        UpdateMembershipRoleResponse response = await _handler.Handle(
            BuildCommand(tenantId, targetUserId, requesterId, Role.Manager),
            CancellationToken.None);

        // Assert
        targetMembership.Role.Should().Be(Role.Manager);
        response.MembershipId.Should().Be(targetMembership.Id);
        response.UserId.Should().Be(targetUserId);
        response.Role.Should().Be("Manager");

        _membershipRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenPromotingManagerToAdministrator_SkipsAdministratorCountCheck()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid requesterId = Guid.NewGuid();
        Guid targetUserId = Guid.NewGuid();

        TenantMembership targetMembership = BuildMembership(targetUserId, tenantId, Role.Manager);

        _membershipRepository
            .Setup(r => r.FindActiveByUserAndTenantAsync(requesterId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildMembership(requesterId, tenantId, Role.Administrator));
        _membershipRepository
            .Setup(r => r.FindActiveByUserAndTenantAsync(targetUserId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetMembership);
        _membershipRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        UpdateMembershipRoleResponse response = await _handler.Handle(
            BuildCommand(tenantId, targetUserId, requesterId, Role.Administrator),
            CancellationToken.None);

        // Assert
        targetMembership.Role.Should().Be(Role.Administrator);
        response.Role.Should().Be("Administrator");

        _membershipRepository.Verify(
            r => r.CountAdministratorsByTenantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _membershipRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenAdministratorKeepsAdministratorRole_SkipsAdministratorCountCheck()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid requesterId = Guid.NewGuid();
        Guid targetUserId = Guid.NewGuid();

        TenantMembership targetMembership = BuildMembership(targetUserId, tenantId, Role.Administrator);

        _membershipRepository
            .Setup(r => r.FindActiveByUserAndTenantAsync(requesterId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildMembership(requesterId, tenantId, Role.Administrator));
        _membershipRepository
            .Setup(r => r.FindActiveByUserAndTenantAsync(targetUserId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetMembership);
        _membershipRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        UpdateMembershipRoleResponse response = await _handler.Handle(
            BuildCommand(tenantId, targetUserId, requesterId, Role.Administrator),
            CancellationToken.None);

        // Assert
        targetMembership.Role.Should().Be(Role.Administrator);
        response.Role.Should().Be("Administrator");

        _membershipRepository.Verify(
            r => r.CountAdministratorsByTenantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _membershipRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
