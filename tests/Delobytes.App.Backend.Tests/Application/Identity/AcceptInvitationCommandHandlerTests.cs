using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Identity.Application.Commands.AcceptInvitation;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity;

/// <summary>
/// Unit tests for AcceptInvitationCommandHandler.
/// </summary>
public class AcceptInvitationCommandHandlerTests
{
    private readonly Mock<IInvitationRepository> _invitationRepo;
    private readonly Mock<ITenantMembershipRepository> _membershipRepo;
    private readonly Mock<IUserRepository> _userRepo;
    private readonly Mock<IJwtTokenService> _jwtService;

    public AcceptInvitationCommandHandlerTests()
    {
        _invitationRepo = new Mock<IInvitationRepository>();
        _membershipRepo = new Mock<ITenantMembershipRepository>();
        _userRepo = new Mock<IUserRepository>();
        _jwtService = new Mock<IJwtTokenService>();
    }

    private AcceptInvitationCommandHandler BuildHandler()
        => new AcceptInvitationCommandHandler(
            _invitationRepo.Object,
            _membershipRepo.Object,
            _userRepo.Object,
            _jwtService.Object);

    private static AcceptInvitationCommand BuildCommand(string token, Guid userId)
        => new AcceptInvitationCommand
        {
            Token = token,
            UserId = userId,
        };

    private static Invitation BuildInvitation(Guid tenantId, string email, string token = "valid-token", Role role = Role.Manager)
        => new Invitation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = email,
            Role = role,
            Token = token,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            IsAccepted = false,
        };

    private static User BuildUser(Guid userId, string email)
        => new User
        {
            Id = userId,
            Email = email,
            ExternalId = "ext-001",
            IdentityProvider = "GoogleID",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static Tenant BuildTenant(Guid tenantId, string name = "Test Tenant")
        => new Tenant
        {
            Id = tenantId,
            Name = name,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    // ── Token validation ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_InvalidToken_ThrowsInvalidOperationException()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        AcceptInvitationCommand command = BuildCommand("invalid-token", userId);

        _invitationRepo
            .Setup(r => r.FindByTokenAsync("invalid-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Invitation?)null);

        // Act & Assert
        Func<Task> act = async () => await BuildHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*не найдено*");
    }

    [Fact]
    public async Task Handle_AlreadyAcceptedInvitation_ThrowsInvalidOperationException()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        string email = "user@example.com";

        AcceptInvitationCommand command = BuildCommand("accepted-token", userId);

        Invitation invitation = BuildInvitation(tenantId, email, "accepted-token");
        invitation.IsAccepted = true;
        invitation.AcceptedAt = DateTimeOffset.UtcNow.AddDays(-1);

        _invitationRepo
            .Setup(r => r.FindByTokenAsync("accepted-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        // Act & Assert
        Func<Task> act = async () => await BuildHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*уже принято*");
    }

    [Fact]
    public async Task Handle_ExpiredInvitation_ThrowsInvalidOperationException()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        string email = "user@example.com";

        AcceptInvitationCommand command = BuildCommand("expired-token", userId);

        Invitation invitation = BuildInvitation(tenantId, email, "expired-token");
        invitation.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);

        _invitationRepo
            .Setup(r => r.FindByTokenAsync("expired-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        // Act & Assert
        Func<Task> act = async () => await BuildHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*истёк*");
    }

    // ── User validation ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_UserNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();

        AcceptInvitationCommand command = BuildCommand("valid-token", userId);

        Invitation invitation = BuildInvitation(tenantId, "user@example.com");

        _invitationRepo
            .Setup(r => r.FindByTokenAsync("valid-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        _userRepo
            .Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act & Assert
        Func<Task> act = async () => await BuildHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Пользователь не найден*");
    }

    [Fact]
    public async Task Handle_EmailMismatch_ThrowsInvalidOperationException()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        string invitationEmail = "invited@example.com";
        string userEmail = "different@example.com";

        AcceptInvitationCommand command = BuildCommand("valid-token", userId);

        Invitation invitation = BuildInvitation(tenantId, invitationEmail);
        User user = BuildUser(userId, userEmail);

        _invitationRepo
            .Setup(r => r.FindByTokenAsync("valid-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        _userRepo
            .Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act & Assert
        Func<Task> act = async () => await BuildHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*для другого email*");
    }

    // ── Tenant isolation checks ───────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_UserAlreadyMemberOfTargetTenant_ThrowsInvalidOperationException()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        string email = "user@example.com";

        AcceptInvitationCommand command = BuildCommand("valid-token", userId);

        Invitation invitation = BuildInvitation(tenantId, email);
        User user = BuildUser(userId, email);

        _invitationRepo
            .Setup(r => r.FindByTokenAsync("valid-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        _userRepo
            .Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        TenantMembership existingMembership = new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = tenantId,
            Role = Role.Manager,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(userId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingMembership);

        // Act & Assert
        Func<Task> act = async () => await BuildHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*уже являетесь членом*");
    }

    [Fact]
    public async Task Handle_UserCannotAcceptInvitationForDifferentTenant_EvenIfAdminInOtherTenant()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenant1Id = Guid.NewGuid();
        Guid tenant2Id = Guid.NewGuid();
        string email = "admin@example.com";

        // User is admin of tenant1, invitation is for tenant2
        AcceptInvitationCommand command = BuildCommand("valid-token", userId);

        Invitation invitationForTenant2 = BuildInvitation(tenant2Id, email);
        User user = BuildUser(userId, email);

        _invitationRepo
            .Setup(r => r.FindByTokenAsync("valid-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitationForTenant2);

        _userRepo
            .Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(userId, tenant2Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantMembership?)null);

        invitationForTenant2.Tenant = BuildTenant(tenant2Id, "Tenant 2");

        TenantMembership? capturedMembership = null;
        _membershipRepo
            .Setup(r => r.Add(It.IsAny<TenantMembership>()))
            .Callback<TenantMembership>(m => capturedMembership = m);

        _membershipRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _jwtService
            .Setup(s => s.GenerateToken(userId, tenant2Id, It.IsAny<Role>()))
            .Returns("jwt-token");

        // Act
        AcceptInvitationResponse response = await BuildHandler().Handle(command, CancellationToken.None);

        // Assert
        capturedMembership.Should().NotBeNull();
        capturedMembership!.TenantId.Should().Be(tenant2Id);
        capturedMembership.UserId.Should().Be(userId);

        response.TenantId.Should().Be(tenant2Id);
    }

    // ── Successful invitation acceptance ──────────────────────────────────────────────

    [Fact]
    public async Task Handle_ValidInvitation_CreatesMembershipWithCorrectFields()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        string email = "newmember@example.com";
        Role role = Role.Manager;

        AcceptInvitationCommand command = BuildCommand("valid-token", userId);

        Invitation invitation = BuildInvitation(tenantId, email, "valid-token", role);
        invitation.Tenant = BuildTenant(tenantId);
        User user = BuildUser(userId, email);

        _invitationRepo
            .Setup(r => r.FindByTokenAsync("valid-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        _userRepo
            .Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(userId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantMembership?)null);

        TenantMembership? capturedMembership = null;
        _membershipRepo
            .Setup(r => r.Add(It.IsAny<TenantMembership>()))
            .Callback<TenantMembership>(m => capturedMembership = m);

        _membershipRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _jwtService
            .Setup(s => s.GenerateToken(userId, tenantId, role))
            .Returns("jwt-token");

        // Act
        AcceptInvitationResponse response = await BuildHandler().Handle(command, CancellationToken.None);

        // Assert
        capturedMembership.Should().NotBeNull();
        capturedMembership!.UserId.Should().Be(userId);
        capturedMembership.TenantId.Should().Be(tenantId);
        capturedMembership.Role.Should().Be(role);
        capturedMembership.IsActive.Should().BeTrue();
        capturedMembership.Id.Should().NotBeEmpty();

        _membershipRepo.Verify(r => r.Add(It.IsAny<TenantMembership>()), Times.Once);
        _membershipRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidInvitation_MarksInvitationAsAccepted()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        string email = "user@example.com";

        AcceptInvitationCommand command = BuildCommand("valid-token", userId);

        Invitation invitation = BuildInvitation(tenantId, email);
        invitation.Tenant = BuildTenant(tenantId);
        User user = BuildUser(userId, email);

        _invitationRepo
            .Setup(r => r.FindByTokenAsync("valid-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        _userRepo
            .Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(userId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantMembership?)null);

        _membershipRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _jwtService.Setup(s => s.GenerateToken(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Role>())).Returns("jwt");

        // Act
        await BuildHandler().Handle(command, CancellationToken.None);

        // Assert
        invitation.IsAccepted.Should().BeTrue();
        invitation.AcceptedAt.Should().NotBeNull();
        invitation.AcceptedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        invitation.AcceptedByUserId.Should().Be(userId);
    }

    [Fact]
    public async Task Handle_ValidInvitation_ReturnsCorrectResponse()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        string email = "user@example.com";
        string tenantName = "My Workspace";
        Role role = Role.ReadOnly;

        AcceptInvitationCommand command = BuildCommand("valid-token", userId);

        Invitation invitation = BuildInvitation(tenantId, email, "valid-token", role);
        invitation.Tenant = BuildTenant(tenantId, tenantName);
        User user = BuildUser(userId, email);

        _invitationRepo
            .Setup(r => r.FindByTokenAsync("valid-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        _userRepo
            .Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(userId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantMembership?)null);

        _membershipRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        string expectedToken = "jwt-access-token";
        _jwtService
            .Setup(s => s.GenerateToken(userId, tenantId, role))
            .Returns(expectedToken);

        // Act
        AcceptInvitationResponse response = await BuildHandler().Handle(command, CancellationToken.None);

        // Assert
        response.TenantId.Should().Be(tenantId);
        response.TenantName.Should().Be(tenantName);
        response.Role.Should().Be(role.ToString());
        response.AccessToken.Should().Be(expectedToken);

        _jwtService.Verify(s => s.GenerateToken(userId, tenantId, role), Times.Once);
    }

    [Fact]
    public async Task Handle_EmailComparisonIsCaseInsensitive()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        string invitationEmail = "User@Example.COM";
        string userEmail = "user@example.com";

        AcceptInvitationCommand command = BuildCommand("valid-token", userId);

        Invitation invitation = BuildInvitation(tenantId, invitationEmail);
        invitation.Tenant = BuildTenant(tenantId);
        User user = BuildUser(userId, userEmail);

        _invitationRepo
            .Setup(r => r.FindByTokenAsync("valid-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        _userRepo
            .Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(userId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantMembership?)null);

        _membershipRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _jwtService.Setup(s => s.GenerateToken(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Role>())).Returns("jwt");

        // Act
        Func<Task> act = async () => await BuildHandler().Handle(command, CancellationToken.None);

        // Assert - should not throw
        await act.Should().NotThrowAsync();

        _membershipRepo.Verify(r => r.Add(It.IsAny<TenantMembership>()), Times.Once);
    }
}
