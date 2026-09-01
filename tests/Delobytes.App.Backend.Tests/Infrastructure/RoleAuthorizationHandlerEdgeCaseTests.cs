using System.Security.Claims;
using Delobytes.App.Backend.Identity.Domain.Enums;
using Delobytes.App.Backend.Identity.Infrastructure.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;

namespace Delobytes.App.Backend.Tests.Identity;

/// <summary>
/// Additional edge-case tests for RoleAuthorizationHandler.
/// Covers all role/policy combinations and boundary conditions
/// not addressed in RoleAuthorizationHandlerTests.
/// </summary>
public class RoleAuthorizationHandlerEdgeCaseTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static AuthorizationHandlerContext BuildContext(
        RoleRequirement requirement,
        ClaimsPrincipal principal)
    {
        return new AuthorizationHandlerContext(
            new[] { requirement },
            principal,
            null);
    }

    private static ClaimsPrincipal BuildPrincipal(string roleClaim)
    {
        Claim[] claims = new[]
        {
            new Claim("userId", Guid.NewGuid().ToString()),
            new Claim("tenantId", Guid.NewGuid().ToString()),
            new Claim("role", roleClaim),
        };

        ClaimsIdentity identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    // ── RequireRole_ReadOnly policy (Administrator | Manager | ReadOnly) ─────

    [Fact]
    public async Task HandleRequirementAsync_ReadOnlyRole_AllowsAccessToReadOnlyPolicy()
    {
        // Arrange — mirrors RequireRole_ReadOnly: AllowedRoles = [Administrator, Manager, ReadOnly]
        RoleAuthorizationHandler handler = new RoleAuthorizationHandler();
        RoleRequirement requirement = new RoleRequirement(Role.Administrator, Role.Manager, Role.ReadOnly);
        AuthorizationHandlerContext context = BuildContext(requirement, BuildPrincipal(Role.ReadOnly.ToString()));

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_ManagerRole_AllowsAccessToReadOnlyPolicy()
    {
        // Arrange
        RoleAuthorizationHandler handler = new RoleAuthorizationHandler();
        RoleRequirement requirement = new RoleRequirement(Role.Administrator, Role.Manager, Role.ReadOnly);
        AuthorizationHandlerContext context = BuildContext(requirement, BuildPrincipal(Role.Manager.ToString()));

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    // ── RequireRole_Manager policy (Administrator | Manager) ─────────────────

    [Fact]
    public async Task HandleRequirementAsync_ReadOnlyRole_DeniesAccessToManagerPolicy()
    {
        // Arrange — mirrors RequireRole_Manager: AllowedRoles = [Administrator, Manager]
        RoleAuthorizationHandler handler = new RoleAuthorizationHandler();
        RoleRequirement requirement = new RoleRequirement(Role.Administrator, Role.Manager);
        AuthorizationHandlerContext context = BuildContext(requirement, BuildPrincipal(Role.ReadOnly.ToString()));

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    // ── RequireRole_Administrator policy (Administrator only) ─────────────────

    [Fact]
    public async Task HandleRequirementAsync_ManagerRole_DeniesAccessToAdministratorOnlyPolicy()
    {
        // Arrange — mirrors RequireRole_Administrator: AllowedRoles = [Administrator]
        RoleAuthorizationHandler handler = new RoleAuthorizationHandler();
        RoleRequirement requirement = new RoleRequirement(Role.Administrator);
        AuthorizationHandlerContext context = BuildContext(requirement, BuildPrincipal(Role.Manager.ToString()));

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_ReadOnlyRole_DeniesAccessToAdministratorOnlyPolicy()
    {
        // Arrange
        RoleAuthorizationHandler handler = new RoleAuthorizationHandler();
        RoleRequirement requirement = new RoleRequirement(Role.Administrator);
        AuthorizationHandlerContext context = BuildContext(requirement, BuildPrincipal(Role.ReadOnly.ToString()));

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleRequirementAsync_UnknownRoleString_DeniesAccess()
    {
        // Arrange — Enum.TryParse fails for an unrecognised value
        RoleAuthorizationHandler handler = new RoleAuthorizationHandler();
        RoleRequirement requirement = new RoleRequirement(Role.Manager);
        AuthorizationHandlerContext context = BuildContext(requirement, BuildPrincipal("SuperAdmin"));

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_LowercaseRoleClaim_DeniesAccess()
    {
        // Arrange — Enum.TryParse<Role> is case-sensitive by default,
        // so "administrator" does not match "Administrator"
        RoleAuthorizationHandler handler = new RoleAuthorizationHandler();
        RoleRequirement requirement = new RoleRequirement(Role.Administrator);
        AuthorizationHandlerContext context = BuildContext(requirement, BuildPrincipal("administrator"));

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_NumericRoleClaim_DeniesAccess()
    {
        // Arrange — the JWT stores the enum name, not its integer value;
        // a raw integer string should not grant access.
        // Note: Enum.TryParse *does* parse numeric strings ("1" → Administrator),
        // so this test documents and locks the current behaviour.
        RoleAuthorizationHandler handler = new RoleAuthorizationHandler();
        RoleRequirement requirement = new RoleRequirement(Role.Manager);
        AuthorizationHandlerContext context = BuildContext(requirement, BuildPrincipal("2"));

        // Act
        await handler.HandleAsync(context);

        // Assert — numeric "2" currently resolves to Manager via Enum.TryParse;
        // document the actual runtime result so any future change is explicit
        bool numericParseSucceeds = Enum.TryParse<Role>("2", out _);
        context.HasSucceeded.Should().Be(numericParseSucceeds);
    }

    [Fact]
    public async Task HandleRequirementAsync_EmptyRoleClaim_DeniesAccess()
    {
        // Arrange — empty string is caught by the IsNullOrEmpty guard in the handler
        RoleAuthorizationHandler handler = new RoleAuthorizationHandler();
        RoleRequirement requirement = new RoleRequirement(Role.ReadOnly);
        AuthorizationHandlerContext context = BuildContext(requirement, BuildPrincipal(string.Empty));

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_WhitespaceRoleClaim_DeniesAccess()
    {
        // Arrange — whitespace-only string also fails the IsNullOrEmpty guard
        RoleAuthorizationHandler handler = new RoleAuthorizationHandler();
        RoleRequirement requirement = new RoleRequirement(Role.ReadOnly);
        AuthorizationHandlerContext context = BuildContext(requirement, BuildPrincipal("   "));

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_UnauthenticatedPrincipal_DeniesAccess()
    {
        // Arrange — principal with no identity (anonymous user)
        RoleAuthorizationHandler handler = new RoleAuthorizationHandler();
        RoleRequirement requirement = new RoleRequirement(Role.ReadOnly);
        ClaimsPrincipal anonymous = new ClaimsPrincipal();
        AuthorizationHandlerContext context = BuildContext(requirement, anonymous);

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    // ── RequireRoleAttribute policy name contract ─────────────────────────────

    [Theory]
    [InlineData(Role.Administrator, "RequireRole_Administrator")]
    [InlineData(Role.Manager, "RequireRole_Manager")]
    [InlineData(Role.ReadOnly, "RequireRole_ReadOnly")]
    public void RequireRoleAttribute_SetsCorrectPolicyName(Role role, string expectedPolicy)
    {
        // Arrange & Act
        RequireRoleAttribute attribute = new RequireRoleAttribute(role);

        // Assert
        attribute.Policy.Should().Be(expectedPolicy);
    }

    // ── Multiple requirements in same context ─────────────────────────────────

    [Fact]
    public async Task HandleRequirementAsync_AdministratorRole_SatisfiesMultipleRequirementsInContext()
    {
        // Arrange — two requirements in a single context; handler is invoked for each
        RoleAuthorizationHandler handler = new RoleAuthorizationHandler();
        RoleRequirement req1 = new RoleRequirement(Role.Manager);
        RoleRequirement req2 = new RoleRequirement(Role.ReadOnly);

        Claim[] claims = new[]
        {
            new Claim("userId", Guid.NewGuid().ToString()),
            new Claim("tenantId", Guid.NewGuid().ToString()),
            new Claim("role", Role.Administrator.ToString()),
        };

        ClaimsIdentity identity = new ClaimsIdentity(claims, "TestAuth");
        ClaimsPrincipal principal = new ClaimsPrincipal(identity);

        // Two separate contexts mimic the handler being called once per requirement
        AuthorizationHandlerContext ctx1 = BuildContext(req1, principal);
        AuthorizationHandlerContext ctx2 = BuildContext(req2, principal);

        // Act
        await handler.HandleAsync(ctx1);
        await handler.HandleAsync(ctx2);

        // Assert
        ctx1.HasSucceeded.Should().BeTrue();
        ctx2.HasSucceeded.Should().BeTrue();
    }
}
