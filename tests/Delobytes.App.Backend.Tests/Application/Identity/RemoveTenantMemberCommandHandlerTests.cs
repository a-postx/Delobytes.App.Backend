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

public class RemoveTenantMemberCommandHandlerTests
{
    private readonly Mock<ITenantMembershipRepository> _membershipRepository;
    private readonly RemoveTenantMemberCommandHandler _handler;

    public RemoveTenantMemberCommandHandlerTests()
    {
        _membershipRepository = new Mock<ITenantMembershipRepository>();
        _handler = new RemoveTenantMemberCommandHandler(_membershipRepository.Object);
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

    private static RemoveTenantMemberCommand BuildCommand(Guid tenantId, Guid targetUserId, Guid removedByUserId)
    {
        return new RemoveTenantMemberCommand
        {
            TenantId = tenantId,
            TargetUserId = targetUserId,
            RemovedByUserId = removedByUserId,
        };
    }

    [Fact]
    public void AllowedRoles_IsAdministratorOnly()
    {
        // Act
        Role[] roles = new RemoveTenantMemberCommand().AllowedRoles;

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
            BuildCommand(tenantId, Guid.NewGuid(), requesterId),
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Только администратор может удалять пользователей из тенанта.");

        _membershipRepository.Verify(r => r.Remove(It.IsAny<TenantMembership>()), Times.Never);
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
            .ReturnsAsync(BuildMembership(requesterId, tenantId, Role.Manager));

        // Act
        Func<Task> act = async () => await _handler.Handle(
            BuildCommand(tenantId, Guid.NewGuid(), requesterId),
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Только администратор может удалять пользователей из тенанта.");

        _membershipRepository.Verify(r => r.Remove(It.IsAny<TenantMembership>()), Times.Never);
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
            BuildCommand(tenantId, targetUserId, requesterId),
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Пользователь не является членом данного тенанта.");

        _membershipRepository.Verify(r => r.Remove(It.IsAny<TenantMembership>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenTargetIsLastAdministrator_ThrowsInvalidOperationException()
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
            .ReturnsAsync(BuildMembership(targetUserId, tenantId, Role.Administrator));
        _membershipRepository
            .Setup(r => r.CountAdministratorsByTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        Func<Task> act = async () => await _handler.Handle(
            BuildCommand(tenantId, targetUserId, requesterId),
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Невозможно удалить последнего администратора тенанта.");

        _membershipRepository.Verify(r => r.Remove(It.IsAny<TenantMembership>()), Times.Never);
        _membershipRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenTargetAdministratorAndAnotherAdminExists_RemovesMembership()
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
        RemoveTenantMemberResponse response = await _handler.Handle(
            BuildCommand(tenantId, targetUserId, requesterId),
            CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        _membershipRepository.Verify(r => r.Remove(targetMembership), Times.Once);
        _membershipRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenTargetIsManager_RemovesWithoutCheckingAdministratorCount()
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
        RemoveTenantMemberResponse response = await _handler.Handle(
            BuildCommand(tenantId, targetUserId, requesterId),
            CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        _membershipRepository.Verify(r => r.Remove(targetMembership), Times.Once);
        _membershipRepository.Verify(
            r => r.CountAdministratorsByTenantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
