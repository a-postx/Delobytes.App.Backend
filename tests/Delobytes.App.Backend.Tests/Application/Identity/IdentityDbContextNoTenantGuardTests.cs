using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity;

/// <summary>
/// Verifies that the "no tenant" guard added to SetTenantId() (see CatalogDbContext,
/// IntegrationsDbContext, SalesDbContext, IdentityDbContext) does NOT break the
/// registration / first-tenant-creation flow.
///
/// During Register and the subsequent CreateTenant call, the user only holds a setup
/// JWT with no "tenantId" claim (see RegisterCommandHandler.GenerateToken(user.Id, null,
/// null) and JwtTokenService, which omits the claim entirely when tenantId is null).
/// ITenantContext.TenantId is therefore null for the whole duration of this flow.
///
/// This must stay safe because none of the Identity entities (User, Tenant,
/// TenantMembership, Invitation) implement ITenantScoped — the guard only fires for
/// entities marked with that interface, so it must not fire here at all.
/// </summary>
public class IdentityDbContextNoTenantGuardTests
{
    private static DbContextOptions<IdentityDbContext> BuildOptions()
    {
        return new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase("identity-no-tenant-guard-" + Guid.NewGuid())
            .Options;
    }

    private static Mock<ITenantContext> NoTenant()
    {
        Mock<ITenantContext> mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns((Guid?)null);
        return mock;
    }

    /// <summary>
    /// Step 1 of the flow: RegisterCommandHandler adds a User and calls SaveChangesAsync
    /// while the caller is anonymous ([AllowAnonymous]) — there is no JWT at all yet,
    /// so ITenantContext.TenantId is null. User does not implement ITenantScoped.
    /// </summary>
    [Fact]
    public async Task Register_AddingUser_WithNoTenant_DoesNotThrow()
    {
        DbContextOptions<IdentityDbContext> options = BuildOptions();

        using IdentityDbContext ctx = new IdentityDbContext(options, NoTenant().Object);

        User user = new User
        {
            Id = Guid.NewGuid(),
            ExternalId = "user@example.com",
            IdentityProvider = "Local",
            Email = "user@example.com",
            PasswordHash = "hash",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };

        ctx.Users.Add(user);

        Func<Task> act = () => ctx.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// Step 2 of the flow: CreateTenantCommandHandler is called with the setup JWT
    /// (sub claim present, no tenantId claim — [Authorize] only requires a valid JWT,
    /// not a tenant claim). It adds both a Tenant and a TenantMembership in the same
    /// unit of work, still with ITenantContext.TenantId == null. Neither entity
    /// implements ITenantScoped, so the guard must not fire.
    /// </summary>
    [Fact]
    public async Task CreateTenant_AddingTenantAndMembership_WithNoTenant_DoesNotThrow()
    {
        DbContextOptions<IdentityDbContext> options = BuildOptions();

        using IdentityDbContext ctx = new IdentityDbContext(options, NoTenant().Object);

        Guid userId = Guid.NewGuid();
        ctx.Users.Add(new User
        {
            Id = userId,
            ExternalId = "user@example.com",
            IdentityProvider = "Local",
            Email = "user@example.com",
            PasswordHash = "hash",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        });
        await ctx.SaveChangesAsync();

        Tenant tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "My First Company",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };
        ctx.Tenants.Add(tenant);

        TenantMembership membership = new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = tenant.Id,
            Role = Role.Administrator,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };
        ctx.TenantMemberships.Add(membership);

        Func<Task> act = () => ctx.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// End-to-end happy path across both steps, on the same underlying store, confirming
    /// the full register -> create-tenant sequence persists correctly with no tenant
    /// resolved at any point.
    /// </summary>
    [Fact]
    public async Task FullRegisterThenCreateTenantFlow_WithNoTenantThroughout_Succeeds()
    {
        DbContextOptions<IdentityDbContext> options = BuildOptions();

        Guid userId = Guid.NewGuid();

        // --- Register ---
        using (IdentityDbContext ctx = new IdentityDbContext(options, NoTenant().Object))
        {
            ctx.Users.Add(new User
            {
                Id = userId,
                ExternalId = "user@example.com",
                IdentityProvider = "Local",
                Email = "user@example.com",
                PasswordHash = "hash",
                CreatedAt = DateTimeOffset.UtcNow,
                IsActive = true,
            });

            int saved = await ctx.SaveChangesAsync();
            saved.Should().Be(1);
        }

        // --- CreateTenant (new DbContext instance = new request, same "no tenant" state) ---
        Guid tenantId = Guid.NewGuid();
        using (IdentityDbContext ctx = new IdentityDbContext(options, NoTenant().Object))
        {
            ctx.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "My First Company",
                CreatedAt = DateTimeOffset.UtcNow,
                IsActive = true,
            });

            ctx.TenantMemberships.Add(new TenantMembership
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TenantId = tenantId,
                Role = Role.Administrator,
                CreatedAt = DateTimeOffset.UtcNow,
                IsActive = true,
            });

            int saved = await ctx.SaveChangesAsync();
            saved.Should().Be(2);
        }

        // --- Verify everything landed, still queried with no tenant (Identity entities
        // carry no query filter at all since they are not ITenantScoped) ---
        using (IdentityDbContext ctx = new IdentityDbContext(options, NoTenant().Object))
        {
            (await ctx.Users.AnyAsync(u => u.Id == userId)).Should().BeTrue();
            (await ctx.Tenants.AnyAsync(t => t.Id == tenantId)).Should().BeTrue();
            (await ctx.TenantMemberships.AnyAsync(m => m.UserId == userId && m.TenantId == tenantId))
                .Should().BeTrue();
        }
    }
}
