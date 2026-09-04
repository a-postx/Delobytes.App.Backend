using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Identity.Application.Commands.CreateTenant;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Application.Options;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity;

/// <summary>
/// Unit tests for CreateTenantCommandHandler.
/// </summary>
public class CreateTenantCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepo;
    private readonly Mock<ITenantRepository> _tenantRepo;
    private readonly Mock<ITenantMembershipRepository> _membershipRepo;
    private readonly Mock<IJwtTokenService> _jwtService;
    private readonly Mock<IOptions<MultitenancyOptions>> _options;

    public CreateTenantCommandHandlerTests()
    {
        _userRepo = new Mock<IUserRepository>();
        _tenantRepo = new Mock<ITenantRepository>();
        _membershipRepo = new Mock<ITenantMembershipRepository>();
        _jwtService = new Mock<IJwtTokenService>();
        _options = new Mock<IOptions<MultitenancyOptions>>();

        _options.Setup(o => o.Value).Returns(new MultitenancyOptions { MaxTenantsPerUser = 2 });
    }

    private CreateTenantCommandHandler BuildHandler()
        => new CreateTenantCommandHandler(
            _userRepo.Object,
            _tenantRepo.Object,
            _membershipRepo.Object,
            _jwtService.Object,
            _options.Object);

    private static CreateTenantCommand BuildCommand(Guid userId, string tenantName = "New Tenant", Guid? currentTenantId = null)
        => new CreateTenantCommand
        {
            UserId = userId,
            TenantName = tenantName,
            CurrentTenantId = currentTenantId,
        };

    private static User BuildUser(Guid userId)
        => new User
        {
            Id = userId,
            Email = "user@example.com",
            ExternalId = "ext-001",
            IdentityProvider = "GoogleID",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
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

    // ── User validation ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_UserNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        CreateTenantCommand command = BuildCommand(userId);

        _userRepo
            .Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act & Assert
        Func<Task> act = async () => await BuildHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*не найден*");
    }

    // ── Tenant limit validation ───────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_UserReachedMaxTenantLimit_ThrowsInvalidOperationException()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        CreateTenantCommand command = BuildCommand(userId);

        User user = BuildUser(userId);

        _userRepo
            .Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _membershipRepo
            .Setup(r => r.CountActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        _options.Setup(o => o.Value).Returns(new MultitenancyOptions { MaxTenantsPerUser = 2 });

        // Act & Assert
        Func<Task> act = async () => await BuildHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*лимит*");

        _tenantRepo.Verify(r => r.Add(It.IsAny<Tenant>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UserExceedsMaxTenantLimit_ThrowsInvalidOperationExceptionWithLimitValue()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        CreateTenantCommand command = BuildCommand(userId);

        User user = BuildUser(userId);

        _userRepo
            .Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _membershipRepo
            .Setup(r => r.CountActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        _options.Setup(o => o.Value).Returns(new MultitenancyOptions { MaxTenantsPerUser = 5 });

        // Act & Assert
        Func<Task> act = async () => await BuildHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*(5)*");
    }

    // ── First tenant creation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_FirstTenantCreation_DoesNotRequireCurrentTenantId()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        string tenantName = "First Tenant";
        CreateTenantCommand command = BuildCommand(userId, tenantName, currentTenantId: null);

        User user = BuildUser(userId);

        _userRepo
            .Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _membershipRepo
            .Setup(r => r.CountActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _tenantRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _jwtService
            .Setup(s => s.GenerateToken(It.IsAny<Guid>(), It.IsAny<Guid>(), Role.Administrator))
            .Returns("jwt-token");

        // Act
        CreateTenantResponse response = await BuildHandler().Handle(command, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.TenantId.Should().NotBeEmpty();
        response.AccessToken.Should().Be("jwt-token");

        _tenantRepo.Verify(r => r.Add(It.IsAny<Tenant>()), Times.Once);
        _membershipRepo.Verify(r => r.Add(It.IsAny<TenantMembership>()), Times.Once);
    }

    [Fact]
    public async Task Handle_FirstTenantCreation_UserBecomesAdministrator()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        CreateTenantCommand command = BuildCommand(userId);

        User user = BuildUser(userId);

        _userRepo
            .Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _membershipRepo
            .Setup(r => r.CountActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        TenantMembership? capturedMembership = null;
        _membershipRepo
            .Setup(r => r.Add(It.IsAny<TenantMembership>()))
            .Callback<TenantMembership>(m => capturedMembership = m);

        _tenantRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _jwtService.Setup(s => s.GenerateToken(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Role>())).Returns("jwt");

        // Act
        await BuildHandler().Handle(command, CancellationToken.None);

        // Assert
        capturedMembership.Should().NotBeNull();
        capturedMembership!.Role.Should().Be(Role.Administrator);
        capturedMembership.UserId.Should().Be(userId);
        capturedMembership.IsActive.Should().BeTrue();
    }

    // ── Additional tenant creation ────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_AdditionalTenant_RequiresCurrentTenantId()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        CreateTenantCommand command = BuildCommand(userId, currentTenantId: null);

        User user = BuildUser(userId);

        _userRepo
            .Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _membershipRepo
            .Setup(r => r.CountActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act & Assert
        Func<Task> act = async () => await BuildHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*необходимо указать текущий*");
    }

    [Fact]
    public async Task Handle_AdditionalTenant_RequiresAdministratorRole()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid currentTenantId = Guid.NewGuid();
        CreateTenantCommand command = BuildCommand(userId, currentTenantId: currentTenantId);

        User user = BuildUser(userId);

        _userRepo
            .Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _membershipRepo
            .Setup(r => r.CountActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        TenantMembership currentMembership = BuildMembership(userId, currentTenantId, Role.Manager);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(userId, currentTenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentMembership);

        // Act & Assert
        Func<Task> act = async () => await BuildHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Администратор*");

        _tenantRepo.Verify(r => r.Add(It.IsAny<Tenant>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AdditionalTenant_ReadOnlyCannotCreateTenant()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid currentTenantId = Guid.NewGuid();
        CreateTenantCommand command = BuildCommand(userId, currentTenantId: currentTenantId);

        User user = BuildUser(userId);

        _userRepo
            .Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _membershipRepo
            .Setup(r => r.CountActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        TenantMembership currentMembership = BuildMembership(userId, currentTenantId, Role.ReadOnly);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(userId, currentTenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentMembership);

        // Act & Assert
        Func<Task> act = async () => await BuildHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Администратор*");
    }

    [Fact]
    public async Task Handle_AdditionalTenant_UserNotInSpecifiedCurrentTenant_ThrowsInvalidOperationException()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid currentTenantId = Guid.NewGuid();
        CreateTenantCommand command = BuildCommand(userId, currentTenantId: currentTenantId);

        User user = BuildUser(userId);

        _userRepo
            .Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _membershipRepo
            .Setup(r => r.CountActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(userId, currentTenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantMembership?)null);

        // Act & Assert
        Func<Task> act = async () => await BuildHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*не состоит*");
    }

    // ── Tenant isolation checks ───────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_AdditionalTenant_AdminOfTenant1CannotBypassLimitByClaimingTenant2()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid tenant1Id = Guid.NewGuid();
        Guid tenant2Id = Guid.NewGuid();

        // User is admin of tenant1, tries to create additional tenant by claiming to be in tenant2
        CreateTenantCommand command = BuildCommand(userId, currentTenantId: tenant2Id);

        User user = BuildUser(userId);

        _userRepo
            .Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _membershipRepo
            .Setup(r => r.CountActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(userId, tenant2Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantMembership?)null);

        // Act & Assert
        Func<Task> act = async () => await BuildHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();

        _tenantRepo.Verify(r => r.Add(It.IsAny<Tenant>()), Times.Never);
    }

    // ── Successful tenant creation ────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ValidRequest_CreatesTenantWithCorrectFields()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        string tenantName = "My Company";
        CreateTenantCommand command = BuildCommand(userId, tenantName);

        User user = BuildUser(userId);

        _userRepo
            .Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _membershipRepo
            .Setup(r => r.CountActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        Tenant? capturedTenant = null;
        _tenantRepo
            .Setup(r => r.Add(It.IsAny<Tenant>()))
            .Callback<Tenant>(t => capturedTenant = t);

        _tenantRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _jwtService.Setup(s => s.GenerateToken(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Role>())).Returns("jwt");

        // Act
        CreateTenantResponse response = await BuildHandler().Handle(command, CancellationToken.None);

        // Assert
        capturedTenant.Should().NotBeNull();
        capturedTenant!.Name.Should().Be(tenantName);
        capturedTenant.IsActive.Should().BeTrue();
        capturedTenant.Id.Should().NotBeEmpty();
        capturedTenant.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));

        response.TenantId.Should().Be(capturedTenant.Id);

        _tenantRepo.Verify(r => r.Add(It.IsAny<Tenant>()), Times.Once);
        _tenantRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidRequest_GeneratesJwtTokenWithCorrectParameters()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        CreateTenantCommand command = BuildCommand(userId);

        User user = BuildUser(userId);

        _userRepo
            .Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _membershipRepo
            .Setup(r => r.CountActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        Guid? capturedTenantId = null;
        _tenantRepo
            .Setup(r => r.Add(It.IsAny<Tenant>()))
            .Callback<Tenant>(t => capturedTenantId = t.Id);

        _tenantRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        string expectedToken = "expected-jwt-token";
        _jwtService
            .Setup(s => s.GenerateToken(userId, It.IsAny<Guid>(), Role.Administrator))
            .Returns(expectedToken);

        // Act
        CreateTenantResponse response = await BuildHandler().Handle(command, CancellationToken.None);

        // Assert
        response.AccessToken.Should().Be(expectedToken);

        _jwtService.Verify(
            s => s.GenerateToken(userId, capturedTenantId!.Value, Role.Administrator),
            Times.Once);
    }

    [Fact]
    public async Task Handle_AdministratorInCurrentTenant_CanCreateAdditionalTenant()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid currentTenantId = Guid.NewGuid();
        string newTenantName = "Second Workspace";
        CreateTenantCommand command = BuildCommand(userId, newTenantName, currentTenantId);

        User user = BuildUser(userId);

        _userRepo
            .Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _membershipRepo
            .Setup(r => r.CountActiveByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        TenantMembership currentMembership = BuildMembership(userId, currentTenantId, Role.Administrator);

        _membershipRepo
            .Setup(r => r.FindActiveByUserAndTenantAsync(userId, currentTenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentMembership);

        _tenantRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _jwtService.Setup(s => s.GenerateToken(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Role>())).Returns("jwt");

        // Act
        CreateTenantResponse response = await BuildHandler().Handle(command, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.TenantId.Should().NotBeEmpty();
        response.TenantId.Should().NotBe(currentTenantId);

        _tenantRepo.Verify(r => r.Add(It.Is<Tenant>(t => t.Name == newTenantName)), Times.Once);
        _membershipRepo.Verify(r => r.Add(It.Is<TenantMembership>(m => m.UserId == userId && m.TenantId != currentTenantId)), Times.Once);
    }
}
