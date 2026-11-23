using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Infrastructure.Persistence;
using Delobytes.App.Backend.Identity.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Delobytes.App.Backend.Tests.Infrastructure.Identity;

/// <summary>
/// Verifies that email lookups use the normalized value and stay scoped to a provider.
/// </summary>
public class UserRepositoryEmailNormalizationTests
{
    private static IdentityDbContext BuildContext()
    {
        DbContextOptions<IdentityDbContext> options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase("identity-email-normalization-" + Guid.NewGuid())
            .Options;

        Mock<ITenantContext> tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.TenantId).Returns((Guid?)null);

        return new IdentityDbContext(options, tenantContext.Object);
    }

    private static User BuildUser(string email, string normalizedEmail, string provider, string externalId)
        => new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            NormalizedEmail = normalizedEmail,
            ExternalId = externalId,
            IdentityProvider = provider,
            PasswordHash = "hash",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };

    [Fact]
    public async Task FindByEmailAsync_DifferentCasingAndWhitespace_FindsLocalUser()
    {
        // Arrange
        using IdentityDbContext context = BuildContext();
        User localUser = BuildUser("User@Example.com", "user@example.com", "Local", "user@example.com");
        context.Users.Add(localUser);
        context.Users.Add(BuildUser("Other@Example.com", "other@example.com", "GoogleID", "google-1"));
        await context.SaveChangesAsync();

        UserRepository repository = new UserRepository(context);

        // Act
        User? found = await repository.FindByEmailAsync(" USER@EXAMPLE.COM ", "Local", CancellationToken.None);

        // Assert
        found.Should().NotBeNull();
        found!.Id.Should().Be(localUser.Id);
    }

    [Fact]
    public async Task FindByEmailAsync_SameEmailOtherProvider_ReturnsNull()
    {
        // Arrange
        using IdentityDbContext context = BuildContext();
        context.Users.Add(BuildUser("User@Example.com", "user@example.com", "Local", "user@example.com"));
        context.Users.Add(BuildUser("User@Example.com", "user@example.com", "GoogleID", "google-1"));
        await context.SaveChangesAsync();

        UserRepository repository = new UserRepository(context);

        // Act
        User? found = await repository.FindByEmailAsync("user@example.com", "YandexID", CancellationToken.None);

        // Assert
        found.Should().BeNull();
    }

    [Fact]
    public async Task FindByEmailAsync_AnyProvider_UsesNormalizedValue()
    {
        // Arrange
        using IdentityDbContext context = BuildContext();
        context.Users.Add(BuildUser("Other@Example.com", "other@example.com", "GoogleID", "google-1"));
        await context.SaveChangesAsync();

        UserRepository repository = new UserRepository(context);

        // Act
        User? found = await repository.FindByEmailAsync(" OTHER@EXAMPLE.COM ", CancellationToken.None);

        // Assert
        found.Should().NotBeNull();
        found!.IdentityProvider.Should().Be("GoogleID");
    }
}
