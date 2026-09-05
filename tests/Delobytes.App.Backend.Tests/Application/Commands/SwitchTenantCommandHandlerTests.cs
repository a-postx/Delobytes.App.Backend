using Delobytes.App.Backend.Identity.Application.Commands.SwitchTenant;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Domain.Enums;
using Moq;

namespace Delobytes.App.Backend.Identity.Tests.Commands;

public class SwitchTenantCommandHandlerTests
{
    private readonly Mock<ITenantMembershipRepository> _membershipRepositoryMock;
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IJwtTokenService> _jwtTokenServiceMock;
    private readonly SwitchTenantCommandHandler _handler;

    public SwitchTenantCommandHandlerTests()
    {
        _membershipRepositoryMock = new Mock<ITenantMembershipRepository>();
        _userRepositoryMock = new Mock<IUserRepository>();
        _jwtTokenServiceMock = new Mock<IJwtTokenService>();

        _handler = new SwitchTenantCommandHandler(
            _membershipRepositoryMock.Object,
            _userRepositoryMock.Object,
            _jwtTokenServiceMock.Object);
    }

    [Fact]
    public async Task Handle_WhenSwitchingTenant_ShouldUpdateLastActiveTenantId()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid sourceTenantId = Guid.NewGuid();
        Guid targetTenantId = Guid.NewGuid();

        User user = new User
        {
            Id = userId,
            Email = "test@example.com",
            ExternalId = "test@example.com",
            IdentityProvider = "Local",
            IsActive = true,
            LastActiveTenantId = sourceTenantId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        TenantMembership targetMembership = new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = targetTenantId,
            Role = Role.Administrator,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        SwitchTenantCommand command = new SwitchTenantCommand
        {
            UserId = userId,
            TargetTenantId = targetTenantId
        };

        _membershipRepositoryMock
            .Setup(x => x.FindActiveByUserAndTenantAsync(userId, targetTenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetMembership);

        _userRepositoryMock
            .Setup(x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _userRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _jwtTokenServiceMock
            .Setup(x => x.GenerateToken(userId, targetTenantId, Role.Administrator))
            .Returns("new_jwt_token");

        // Act
        SwitchTenantResponse response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(response);
        Assert.Equal("new_jwt_token", response.AccessToken);
        Assert.Equal(targetTenantId, user.LastActiveTenantId);

        _userRepositoryMock.Verify(
            x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>()),
            Times.Once);

        _userRepositoryMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenMembershipNotFound_ShouldThrowInvalidOperationException()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid targetTenantId = Guid.NewGuid();

        SwitchTenantCommand command = new SwitchTenantCommand
        {
            UserId = userId,
            TargetTenantId = targetTenantId
        };

        _membershipRepositoryMock
            .Setup(x => x.FindActiveByUserAndTenantAsync(userId, targetTenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantMembership?)null);

        // Act & Assert
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.Handle(command, CancellationToken.None));

        Assert.Equal("Пользователь не состоит в указанном пространстве.", exception.Message);

        _userRepositoryMock.Verify(
            x => x.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _userRepositoryMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // странный тест, но пусть будет
    [Fact]
    public async Task Handle_WhenUserNotFound_ShouldStillGenerateToken()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid targetTenantId = Guid.NewGuid();

        TenantMembership targetMembership = new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = targetTenantId,
            Role = Role.Administrator,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        SwitchTenantCommand command = new SwitchTenantCommand
        {
            UserId = userId,
            TargetTenantId = targetTenantId
        };

        _membershipRepositoryMock
            .Setup(x => x.FindActiveByUserAndTenantAsync(userId, targetTenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetMembership);

        _userRepositoryMock
            .Setup(x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        _jwtTokenServiceMock
            .Setup(x => x.GenerateToken(userId, targetTenantId, Role.Administrator))
            .Returns("jwt_token");

        // Act
        SwitchTenantResponse response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(response);
        Assert.Equal("jwt_token", response.AccessToken);

        _userRepositoryMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ShouldGenerateTokenWithCorrectRole()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid targetTenantId = Guid.NewGuid();

        User user = new User
        {
            Id = userId,
            Email = "test@example.com",
            ExternalId = "test@example.com",
            IdentityProvider = "Local",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        TenantMembership targetMembership = new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = targetTenantId,
            Role = Role.Administrator,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        SwitchTenantCommand command = new SwitchTenantCommand
        {
            UserId = userId,
            TargetTenantId = targetTenantId
        };

        _membershipRepositoryMock
            .Setup(x => x.FindActiveByUserAndTenantAsync(userId, targetTenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetMembership);

        _userRepositoryMock
            .Setup(x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _userRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _jwtTokenServiceMock
            .Setup(x => x.GenerateToken(userId, targetTenantId, Role.Administrator))
            .Returns("admin_jwt_token");

        // Act
        SwitchTenantResponse response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal("admin_jwt_token", response.AccessToken);

        _jwtTokenServiceMock.Verify(
            x => x.GenerateToken(userId, targetTenantId, Role.Administrator),
            Times.Once);
    }
}
