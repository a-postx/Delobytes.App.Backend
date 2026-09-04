using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Identity.Application.Commands.CreateInvitation;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity;

/// <summary>
/// Unit tests for CreateInvitationCommandHandler.
/// </summary>
public class CreateInvitationCommandHandlerTests
{
    private readonly Mock<IInvitationRepository> _invitationRepo;
    private readonly Mock<ITenantMembershipRepository> _membershipRepo;
    private readonly Mock<IUserRepository> _userRepo;

    public CreateInvitationCommandHandlerTests()
    {
        _invitationRepo = new Mock<IInvitationRepository>();
        _membershipRepo = new Mock<ITenantMembershipRepository>();
        _userRepo = new Mock<IUserRepository>();
    }

    private CreateInvitationCommandHandler BuildHandler()
        => new CreateInvitationCommandHandler(
            _invitationRepo.Object,
            _membershipRepo.Object,
            _userRepo.Object);

    private static CreateInvitationCommand BuildCommand(Guid tenantId, Guid invitedByUserId, string email = "invitee@example.com", Role role = Role.Manager)
        => new CreateInvitationCommand
        {
            TenantId = tenantId,
            InvitedByUserId = invitedByUserId,
            Email = email,
            Role = role,
        };

    private static TenantMembership BuildMembership(Guid userId, Guid tenantId, Role role = Role.Administrator, bool isActive = true)
        => new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = tenantId,
            Role = role,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    // ── Authorization checks ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_InviterNotMemberOfTenant_ThrowsInvalidOperationException()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid inviterId = Guid.NewGuid();
        CreateInvitationCommand command = BuildCommand(tenantId, inviterId);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(inviterId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantMembership?)null);

        // Act & Assert
        Func<Task> act = async () => await BuildHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*администратор*");
    }

    [Fact]
    public async Task Handle_InviterIsNotAdministrator_ThrowsInvalidOperationException()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid inviterId = Guid.NewGuid();
        CreateInvitationCommand command = BuildCommand(tenantId, inviterId);

        TenantMembership inviterMembership = BuildMembership(inviterId, tenantId, Role.Manager);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(inviterId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inviterMembership);

        // Act & Assert
        Func<Task> act = async () => await BuildHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*администратор*");
    }

    [Fact]
    public async Task Handle_InviterIsReadOnly_ThrowsInvalidOperationException()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid inviterId = Guid.NewGuid();
        CreateInvitationCommand command = BuildCommand(tenantId, inviterId);

        TenantMembership inviterMembership = BuildMembership(inviterId, tenantId, Role.ReadOnly);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(inviterId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inviterMembership);

        // Act & Assert
        Func<Task> act = async () => await BuildHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*администратор*");
    }

    // ── Tenant isolation checks ──────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_InviterFromDifferentTenant_CannotCreateInvitationForAnotherTenant()
    {
        // Arrange
        Guid tenant1Id = Guid.NewGuid();
        Guid tenant2Id = Guid.NewGuid();
        Guid inviterId = Guid.NewGuid();

        // User is admin of tenant1, but tries to invite to tenant2
        CreateInvitationCommand command = BuildCommand(tenant2Id, inviterId);

        TenantMembership inviterMembership = BuildMembership(inviterId, tenant1Id, Role.Administrator);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(inviterId, tenant2Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantMembership?)null);

        // Act & Assert
        Func<Task> act = async () => await BuildHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*администратор*");

        _invitationRepo.Verify(r => r.Add(It.IsAny<Invitation>()), Times.Never);
        _invitationRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Validation checks ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_UserAlreadyMemberOfTenant_ThrowsInvalidOperationException()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid inviterId = Guid.NewGuid();
        Guid existingUserId = Guid.NewGuid();
        string email = "existing@example.com";

        CreateInvitationCommand command = BuildCommand(tenantId, inviterId, email);

        TenantMembership inviterMembership = BuildMembership(inviterId, tenantId, Role.Administrator);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(inviterId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inviterMembership);

        User existingUser = new User
        {
            Id = existingUserId,
            Email = email,
            ExternalId = "ext-001",
            IdentityProvider = "GoogleID",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _userRepo
            .Setup(r => r.FindByEmailAsync(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingUser);

        TenantMembership existingMembership = BuildMembership(existingUserId, tenantId);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(existingUserId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingMembership);

        // Act & Assert
        Func<Task> act = async () => await BuildHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*уже является членом*");
    }

    [Fact]
    public async Task Handle_PendingInvitationAlreadyExists_ThrowsInvalidOperationException()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid inviterId = Guid.NewGuid();
        string email = "pending@example.com";

        CreateInvitationCommand command = BuildCommand(tenantId, inviterId, email);

        TenantMembership inviterMembership = BuildMembership(inviterId, tenantId, Role.Administrator);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(inviterId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inviterMembership);

        _userRepo
            .Setup(r => r.FindByEmailAsync(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        Invitation existingInvitation = new Invitation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = email,
            Role = Role.Manager,
            Token = "existing-token",
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            IsAccepted = false,
        };

        _invitationRepo
            .Setup(r => r.FindPendingByTenantAndEmailAsync(tenantId, email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingInvitation);

        // Act & Assert
        Func<Task> act = async () => await BuildHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Активное приглашение*");
    }

    // ── Successful invitation creation ────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ValidRequest_CreatesInvitationWithCorrectFields()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid inviterId = Guid.NewGuid();
        string email = "newuser@example.com";
        Role role = Role.Manager;

        CreateInvitationCommand command = BuildCommand(tenantId, inviterId, email, role);

        TenantMembership inviterMembership = BuildMembership(inviterId, tenantId, Role.Administrator);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(inviterId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inviterMembership);

        _userRepo
            .Setup(r => r.FindByEmailAsync(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        _invitationRepo
            .Setup(r => r.FindPendingByTenantAndEmailAsync(tenantId, email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Invitation?)null);

        Invitation? capturedInvitation = null;
        _invitationRepo
            .Setup(r => r.Add(It.IsAny<Invitation>()))
            .Callback<Invitation>(inv => capturedInvitation = inv);

        _invitationRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        CreateInvitationResponse response = await BuildHandler().Handle(command, CancellationToken.None);

        // Assert
        capturedInvitation.Should().NotBeNull();
        capturedInvitation!.TenantId.Should().Be(tenantId);
        capturedInvitation.Email.Should().Be(email);
        capturedInvitation.Role.Should().Be(role);
        capturedInvitation.Token.Should().NotBeNullOrEmpty();
        capturedInvitation.IsAccepted.Should().BeFalse();
        capturedInvitation.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);

        response.InvitationId.Should().NotBeEmpty();
        response.Token.Should().Be(capturedInvitation.Token);
        response.Email.Should().Be(email);
        response.Role.Should().Be(role.ToString());

        _invitationRepo.Verify(r => r.Add(It.IsAny<Invitation>()), Times.Once);
        _invitationRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidRequest_TokenIsUniqueAndSecure()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid inviterId = Guid.NewGuid();

        CreateInvitationCommand command1 = BuildCommand(tenantId, inviterId, "user1@example.com");
        CreateInvitationCommand command2 = BuildCommand(tenantId, inviterId, "user2@example.com");

        TenantMembership inviterMembership = BuildMembership(inviterId, tenantId, Role.Administrator);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(inviterId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inviterMembership);

        _userRepo
            .Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        _invitationRepo
            .Setup(r => r.FindPendingByTenantAndEmailAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Invitation?)null);

        _invitationRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        string? token1 = null;
        string? token2 = null;

        _invitationRepo
            .Setup(r => r.Add(It.IsAny<Invitation>()))
            .Callback<Invitation>(inv =>
            {
                if (token1 == null)
                {
                    token1 = inv.Token;
                }
                else
                {
                    token2 = inv.Token;
                }
            });

        // Act
        await BuildHandler().Handle(command1, CancellationToken.None);
        await BuildHandler().Handle(command2, CancellationToken.None);

        // Assert
        token1.Should().NotBeNullOrEmpty();
        token2.Should().NotBeNullOrEmpty();
        token1.Should().NotBe(token2);
        token1!.Length.Should().BeGreaterThan(20);
    }

    [Fact]
    public async Task Handle_ExistingUserNotInTenant_CreatesInvitationSuccessfully()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid inviterId = Guid.NewGuid();
        Guid existingUserId = Guid.NewGuid();
        string email = "existinguser@example.com";

        CreateInvitationCommand command = BuildCommand(tenantId, inviterId, email);

        TenantMembership inviterMembership = BuildMembership(inviterId, tenantId, Role.Administrator);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(inviterId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inviterMembership);

        User existingUser = new User
        {
            Id = existingUserId,
            Email = email,
            ExternalId = "ext-002",
            IdentityProvider = "YandexID",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _userRepo
            .Setup(r => r.FindByEmailAsync(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingUser);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(existingUserId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantMembership?)null);

        _invitationRepo
            .Setup(r => r.FindPendingByTenantAndEmailAsync(tenantId, email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Invitation?)null);

        _invitationRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // Act
        CreateInvitationResponse response = await BuildHandler().Handle(command, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Email.Should().Be(email);

        _invitationRepo.Verify(r => r.Add(It.IsAny<Invitation>()), Times.Once);
        _invitationRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
