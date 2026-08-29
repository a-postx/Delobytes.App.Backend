using System.Security.Claims;
using Delobytes.App.Backend.Identity.Domain.Enums;
using Delobytes.App.Backend.Identity.Infrastructure.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;

namespace Delobytes.App.Backend.Tests.Identity;

/// <summary>
/// Tests for RoleAuthorizationHandler.
/// </summary>
public class RoleAuthorizationHandlerTests
{
    [Fact]
    public async Task HandleRequirementAsync_AdministratorRole_AllowsAccess()
    {
        // Arrange
        var handler = new RoleAuthorizationHandler();
        var requirement = new RoleRequirement(Role.Manager);

        var claims = new[]
        {
            new Claim("userId", Guid.NewGuid().ToString()),
            new Claim("tenantId", Guid.NewGuid().ToString()),
            new Claim("role", Role.Administrator.ToString()),
        };

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var claimsPrincipal = new ClaimsPrincipal(identity);
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            claimsPrincipal,
            null);

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_MatchingRole_AllowsAccess()
    {
        // Arrange
        var handler = new RoleAuthorizationHandler();
        var requirement = new RoleRequirement(Role.Manager, Role.Administrator);

        var claims = new[]
        {
            new Claim("userId", Guid.NewGuid().ToString()),
            new Claim("tenantId", Guid.NewGuid().ToString()),
            new Claim("role", Role.Manager.ToString()),
        };

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var claimsPrincipal = new ClaimsPrincipal(identity);
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            claimsPrincipal,
            null);

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_MismatchingRole_DeniesAccess()
    {
        // Arrange
        var handler = new RoleAuthorizationHandler();
        var requirement = new RoleRequirement(Role.Administrator);

        var claims = new[]
        {
            new Claim("userId", Guid.NewGuid().ToString()),
            new Claim("tenantId", Guid.NewGuid().ToString()),
            new Claim("role", Role.ReadOnly.ToString()),
        };

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var claimsPrincipal = new ClaimsPrincipal(identity);
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            claimsPrincipal,
            null);

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_NoRoleClaim_DeniesAccess()
    {
        // Arrange
        var handler = new RoleAuthorizationHandler();
        var requirement = new RoleRequirement(Role.Manager);

        var claims = new[]
        {
            new Claim("userId", Guid.NewGuid().ToString()),
            new Claim("tenantId", Guid.NewGuid().ToString()),
        };

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var claimsPrincipal = new ClaimsPrincipal(identity);
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            claimsPrincipal,
            null);

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }
}
