using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Application.Services;
using Delobytes.App.Backend.Identity.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity;

/// <summary>
/// Tests for the shared session issuer used by login, registration and OAuth callbacks.
/// </summary>
public class UserSessionIssuerTests
{
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<ITenantMembershipRepository> _membershipRepository = new();
    private readonly Mock<IJwtTokenService> _jwtTokenService = new();

    private UserSessionIssuer BuildIssuer()
        => new UserSessionIssuer(
            _userRepository.Object,
            _membershipRepository.Object,
            _jwtTokenService.Object);

    private static User BuildUser(Guid? lastActiveTenantId = null)
        => new User
        {
            Id = Guid.NewGuid(),
            ExternalId = "user@example.com",
            IdentityProvider = "Local",
            Email = "user@example.com",
            NormalizedEmail = "user@example.com",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveTenantId = lastActiveTenantId,
        };

    private static TenantMembership BuildMembership(Guid userId, Guid tenantId, Role role = Role.Manager)
        => new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = tenantId,
            Role = role,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task IssueAsync_UserWithoutMemberships_ReturnsSetupTokenAndSavesOnce()
    {
        // Arrange
        User user = BuildUser();

        _membershipRepository
            .Setup(r => r.GetActiveByUserAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TenantMembership>());
        _userRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _jwtTokenService
            .Setup(j => j.GenerateToken(user.Id, null, null))
            .Returns("setup-token");

        // Act
        Delobytes.App.Backend.Identity.Application.Commands.LoginResponse response =
            await BuildIssuer().IssueAsync(user, CancellationToken.None);

        // Assert
        response.RequiresTenantSetup.Should().BeTrue();
        response.AccessToken.Should().Be("setup-token");
        response.UserId.Should().Be(user.Id);
        response.TenantId.Should().BeNull();
        user.LastLoginAt.Should().NotBeNull();
        user.LastActiveTenantId.Should().BeNull();

        _userRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IssueAsync_UserWithLastActiveTenant_SelectsThatTenant()
    {
        // Arrange
        Guid tenant1Id = Guid.NewGuid();
        Guid tenant2Id = Guid.NewGuid();
        User user = BuildUser(lastActiveTenantId: tenant2Id);

        _membershipRepository
            .Setup(r => r.GetActiveByUserAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                BuildMembership(user.Id, tenant1Id, Role.Administrator),
                BuildMembership(user.Id, tenant2Id, Role.Manager),
            });
        _userRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _jwtTokenService
            .Setup(j => j.GenerateToken(user.Id, tenant2Id, Role.Manager))
            .Returns("jwt");

        // Act
        Delobytes.App.Backend.Identity.Application.Commands.LoginResponse response =
            await BuildIssuer().IssueAsync(user, CancellationToken.None);

        // Assert
        response.TenantId.Should().Be(tenant2Id);
        response.AccessToken.Should().Be("jwt");
        response.RequiresTenantSetup.Should().BeFalse();

        _jwtTokenService.Verify(j => j.GenerateToken(user.Id, tenant2Id, Role.Manager), Times.Once);
    }

    [Fact]
    public async Task IssueAsync_LastActiveTenantNoLongerActive_FallsBackToFirstMembership()
    {
        // Arrange
        Guid tenant1Id = Guid.NewGuid();
        User user = BuildUser(lastActiveTenantId: Guid.NewGuid());

        _membershipRepository
            .Setup(r => r.GetActiveByUserAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { BuildMembership(user.Id, tenant1Id, Role.ReadOnly) });
        _userRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _jwtTokenService
            .Setup(j => j.GenerateToken(user.Id, tenant1Id, Role.ReadOnly))
            .Returns("jwt");

        // Act
        await BuildIssuer().IssueAsync(user, CancellationToken.None);

        // Assert
        user.LastActiveTenantId.Should().Be(tenant1Id);
        _jwtTokenService.Verify(j => j.GenerateToken(user.Id, tenant1Id, Role.ReadOnly), Times.Once);
    }

    [Fact]
    public async Task IssueAsync_UserWithoutLastActiveTenant_SelectsFirstMembershipAndRemembersIt()
    {
        // Arrange
        Guid tenant1Id = Guid.NewGuid();
        Guid tenant2Id = Guid.NewGuid();
        User user = BuildUser();

        _membershipRepository
            .Setup(r => r.GetActiveByUserAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                BuildMembership(user.Id, tenant1Id),
                BuildMembership(user.Id, tenant2Id),
            });
        _userRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _jwtTokenService
            .Setup(j => j.GenerateToken(user.Id, tenant1Id, Role.Manager))
            .Returns("jwt");

        // Act
        await BuildIssuer().IssueAsync(user, CancellationToken.None);

        // Assert
        user.LastActiveTenantId.Should().Be(tenant1Id);
        _userRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
