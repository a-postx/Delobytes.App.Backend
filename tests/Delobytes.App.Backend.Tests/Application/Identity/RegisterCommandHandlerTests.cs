using System;
using System.Collections.Generic;
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

public class RegisterCommandHandlerTests
{
    private const string LocalProvider = "Local";

    private readonly Mock<IUserRepository> _userRepository;
    private readonly Mock<ITenantMembershipRepository> _membershipRepository;
    private readonly Mock<IPasswordHasher> _passwordHasher;
    private readonly Mock<IJwtTokenService> _jwtTokenService;
    private readonly RegisterCommandHandler _handler;

    public RegisterCommandHandlerTests()
    {
        _userRepository = new Mock<IUserRepository>();
        _membershipRepository = new Mock<ITenantMembershipRepository>();
        _passwordHasher = new Mock<IPasswordHasher>();
        _jwtTokenService = new Mock<IJwtTokenService>();

        _handler = new RegisterCommandHandler(
            _userRepository.Object,
            _membershipRepository.Object,
            _passwordHasher.Object,
            _jwtTokenService.Object);
    }

    [Fact]
    public async Task Handle_WhenEmailAlreadyRegistered_ThrowsInvalidOperationException()
    {
        // Arrange
        string email = "taken@example.com";
        User existingUser = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            ExternalId = email,
            IdentityProvider = LocalProvider,
            IsActive = true,
        };

        _userRepository
            .Setup(r => r.FindByEmailAsync(email, LocalProvider, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingUser);

        RegisterCommand command = new RegisterCommand
        {
            Email = email,
            Password = "Secret123!",
        };

        // Act
        Func<Task> act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"Адрес {email} уже существует ¯\\_(ツ)_/¯");

        _userRepository.Verify(r => r.Add(It.IsAny<User>()), Times.Never);
        _userRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _jwtTokenService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenNewUserHasNoMemberships_CreatesLocalUserAndReturnsSetupToken()
    {
        // Arrange
        string email = "new@example.com";
        string password = "Secret123!";
        string passwordHash = "bcrypt-hash";
        DateTimeOffset before = DateTimeOffset.UtcNow;

        User? addedUser = null;
        _userRepository
            .Setup(r => r.FindByEmailAsync(email, LocalProvider, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        _userRepository
            .Setup(r => r.Add(It.IsAny<User>()))
            .Callback<User>(user => addedUser = user);
        _userRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _membershipRepository
            .Setup(r => r.GetActiveByUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TenantMembership>());
        _passwordHasher
            .Setup(h => h.Hash(password))
            .Returns(passwordHash);
        _jwtTokenService
            .Setup(j => j.GenerateToken(It.IsAny<Guid>(), null, null))
            .Returns("setup-token");

        RegisterCommand command = new RegisterCommand
        {
            Email = email,
            Password = password,
            DisplayName = "Иван",
        };

        // Act
        RegisterResponse response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        addedUser.Should().NotBeNull();
        addedUser!.Id.Should().NotBe(Guid.Empty);
        addedUser.ExternalId.Should().Be(email);
        addedUser.IdentityProvider.Should().Be(LocalProvider);
        addedUser.Email.Should().Be(email);
        addedUser.DisplayName.Should().Be("Иван");
        addedUser.PasswordHash.Should().Be(passwordHash);
        addedUser.IsActive.Should().BeTrue();
        addedUser.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTimeOffset.UtcNow);
        addedUser.LastActiveTenantId.Should().BeNull();

        response.UserId.Should().Be(addedUser.Id);
        response.AccessToken.Should().Be("setup-token");
        response.Success.Should().BeTrue();
        response.RequiresTenantSetup.Should().BeTrue();

        _passwordHasher.Verify(h => h.Hash(password), Times.Once);
        _userRepository.Verify(r => r.Add(It.IsAny<User>()), Times.Once);
        _userRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _jwtTokenService.Verify(j => j.GenerateToken(addedUser.Id, null, null), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenDisplayNameNotProvided_FallsBackToEmail()
    {
        // Arrange
        string email = "no-display-name@example.com";

        User? addedUser = null;
        _userRepository
            .Setup(r => r.FindByEmailAsync(email, LocalProvider, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        _userRepository
            .Setup(r => r.Add(It.IsAny<User>()))
            .Callback<User>(user => addedUser = user);
        _membershipRepository
            .Setup(r => r.GetActiveByUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TenantMembership>());
        _passwordHasher
            .Setup(h => h.Hash(It.IsAny<string>()))
            .Returns("bcrypt-hash");
        _jwtTokenService
            .Setup(j => j.GenerateToken(It.IsAny<Guid>(), null, null))
            .Returns("setup-token");

        RegisterCommand command = new RegisterCommand
        {
            Email = email,
            Password = "Secret123!",
        };

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        addedUser.Should().NotBeNull();
        addedUser!.DisplayName.Should().Be(email);
    }

    [Fact]
    public async Task Handle_WhenNewUserAlreadyHasMembership_ReturnsTokenWithTenantAndRole()
    {
        // Arrange
        string email = "member@example.com";
        Guid tenantId = Guid.NewGuid();

        TenantMembership membership = new TenantMembership
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Role = Role.Manager,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _userRepository
            .Setup(r => r.FindByEmailAsync(email, LocalProvider, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        _userRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _membershipRepository
            .Setup(r => r.GetActiveByUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { membership });
        _passwordHasher
            .Setup(h => h.Hash(It.IsAny<string>()))
            .Returns("bcrypt-hash");
        _jwtTokenService
            .Setup(j => j.GenerateToken(It.IsAny<Guid>(), tenantId, Role.Manager))
            .Returns("full-token");

        RegisterCommand command = new RegisterCommand
        {
            Email = email,
            Password = "Secret123!",
        };

        // Act
        RegisterResponse response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        response.AccessToken.Should().Be("full-token");
        response.Success.Should().BeTrue();
        response.RequiresTenantSetup.Should().BeFalse();

        _jwtTokenService.Verify(j => j.GenerateToken(It.IsAny<Guid>(), tenantId, Role.Manager), Times.Once);
        _jwtTokenService.Verify(j => j.GenerateToken(It.IsAny<Guid>(), null, null), Times.Never);
    }
}
