using System.Security;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Identity.Application.Commands.AcceptInvitation;
using Delobytes.App.Backend.Identity.Application.Commands.CreateInvitation;
using Delobytes.App.Backend.Identity.Application.Commands.RevokeInvitation;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Infrastructure.Persistence;
using Delobytes.App.Backend.Identity.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Identity;

/// <summary>
/// End-to-end verification that Invitation : ITenantScoped does not break the
/// create / accept / revoke invitation flows, using REAL EF Core repositories and
/// command handlers against IdentityDbContext (InMemory) — not mocks — because the
/// interaction under test is precisely the automatic HasQueryFilter and the
/// SetTenantId/ValidateCrossTenantWrite guards, which mocked repositories bypass
/// entirely.
///
/// Background: making Invitation tenant-scoped means every query against
/// Invitations is now implicitly filtered by "TenantId == current tenant". This is
/// correct for CreateInvitation and RevokeInvitation (the acting admin's JWT tenant
/// IS the invitation's tenant). It is actively wrong for AcceptInvitation: the
/// invited user is, by definition, not yet a member of the target tenant, so their
/// own current tenant (or lack of one) legitimately differs from the invitation's
/// TenantId. That path relies on InvitationRepository.FindByTokenAsync using
/// IgnoreQueryFilters(), and on ValidateCrossTenantWrite() explicitly exempting
/// Invitation, both fixed alongside the ITenantScoped change.
/// </summary>
public class InvitationTenantScopedFlowTests
{
    private static DbContextOptions<IdentityDbContext> BuildOptions()
    {
        return new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase("invitation-flow-" + Guid.NewGuid())
            .Options;
    }

    private static Mock<ITenantContext> TenantMock(Guid? tenantId)
    {
        Mock<ITenantContext> mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        return mock;
    }

    private static Mock<IJwtTokenService> FakeJwt()
    {
        Mock<IJwtTokenService> mock = new Mock<IJwtTokenService>();
        mock.Setup(j => j.GenerateToken(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Role?>()))
            .Returns("fake-jwt");
        return mock;
    }

    private static async Task<(Guid tenantId, Guid adminUserId)> SeedTenantWithAdminAsync(
        DbContextOptions<IdentityDbContext> options)
    {
        Guid tenantId = Guid.NewGuid();
        Guid adminUserId = Guid.NewGuid();

        using IdentityDbContext ctx = new IdentityDbContext(options, TenantMock(tenantId).Object);

        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Acme",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        });

        ctx.Users.Add(new User
        {
            Id = adminUserId,
            ExternalId = "admin@acme.com",
            IdentityProvider = "Local",
            Email = "admin@acme.com",
            PasswordHash = "hash",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        });

        ctx.TenantMemberships.Add(new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = adminUserId,
            TenantId = tenantId,
            Role = Role.Administrator,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        });

        await ctx.SaveChangesAsync();

        return (tenantId, adminUserId);
    }

    [Fact]
    public async Task CreateInvitation_AdminActingInOwnTenant_PersistsWithCorrectTenantId()
    {
        DbContextOptions<IdentityDbContext> options = BuildOptions();
        (Guid tenantId, Guid adminUserId) = await SeedTenantWithAdminAsync(options);

        using IdentityDbContext ctx = new IdentityDbContext(options, TenantMock(tenantId).Object);
        CreateInvitationCommandHandler handler = new CreateInvitationCommandHandler(
            new InvitationRepository(ctx),
            new TenantMembershipRepository(ctx),
            new UserRepository(ctx));

        CreateInvitationResponse response = await handler.Handle(
            new CreateInvitationCommand
            {
                TenantId = tenantId,
                Email = "invitee@example.com",
                Role = Role.Manager,
                InvitedByUserId = adminUserId,
            },
            CancellationToken.None);

        response.Should().NotBeNull();

        Guid? storedTenantId = await ctx.Invitations
            .Where(i => i.Id == response.InvitationId)
            .Select(i => (Guid?)i.TenantId)
            .SingleAsync();

        storedTenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task CreateInvitation_NoTenantInContext_ThrowsGuard()
    {
        // Reproduces the exact scenario the SetTenantId guard was built for: an
        // ITenantScoped entity must never be persisted without a resolved tenant.
        DbContextOptions<IdentityDbContext> options = BuildOptions();
        (Guid tenantId, Guid adminUserId) = await SeedTenantWithAdminAsync(options);

        using IdentityDbContext ctx = new IdentityDbContext(options, TenantMock(null).Object);
        CreateInvitationCommandHandler handler = new CreateInvitationCommandHandler(
            new InvitationRepository(ctx),
            new TenantMembershipRepository(ctx),
            new UserRepository(ctx));

        Func<Task> act = () => handler.Handle(
            new CreateInvitationCommand
            {
                TenantId = tenantId,
                Email = "invitee@example.com",
                Role = Role.Manager,
                InvitedByUserId = adminUserId,
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<SecurityException>();
    }

    [Fact]
    public async Task AcceptInvitation_AcceptingUserCurrentlyScopedToDifferentTenant_Succeeds()
    {
        // The realistic multi-tenant-membership case: a user who already belongs to
        // TenantB (their current JWT) accepts an invitation to join TenantA.
        DbContextOptions<IdentityDbContext> options = BuildOptions();
        (Guid tenantA, Guid adminUserId) = await SeedTenantWithAdminAsync(options);

        string token;
        Guid inviteeUserId = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();

        using (IdentityDbContext ctx = new IdentityDbContext(options, TenantMock(tenantA).Object))
        {
            CreateInvitationCommandHandler createHandler = new CreateInvitationCommandHandler(
                new InvitationRepository(ctx),
                new TenantMembershipRepository(ctx),
                new UserRepository(ctx));

            CreateInvitationResponse created = await createHandler.Handle(
                new CreateInvitationCommand
                {
                    TenantId = tenantA,
                    Email = "invitee@example.com",
                    Role = Role.Manager,
                    InvitedByUserId = adminUserId,
                },
                CancellationToken.None);

            token = created.Token;
        }

        // Invitee already exists as a user and already belongs to TenantB — their
        // current session JWT is scoped to TenantB, NOT TenantA.
        using (IdentityDbContext ctx = new IdentityDbContext(options, TenantMock(tenantB).Object))
        {
            ctx.Users.Add(new User
            {
                Id = inviteeUserId,
                ExternalId = "invitee@example.com",
                IdentityProvider = "Local",
                Email = "invitee@example.com",
                PasswordHash = "hash",
                CreatedAt = DateTimeOffset.UtcNow,
                IsActive = true,
            });
            await ctx.SaveChangesAsync();
        }

        using (IdentityDbContext ctx = new IdentityDbContext(options, TenantMock(tenantB).Object))
        {
            AcceptInvitationCommandHandler acceptHandler = new AcceptInvitationCommandHandler(
                new InvitationRepository(ctx),
                new TenantMembershipRepository(ctx),
                new UserRepository(ctx),
                FakeJwt().Object);

            Func<Task<AcceptInvitationResponse>> act = () => acceptHandler.Handle(
                new AcceptInvitationCommand { Token = token, UserId = inviteeUserId },
                CancellationToken.None);

            (await act.Should().NotThrowAsync()).Which.TenantId.Should().Be(tenantA);
        }

        // Verify persisted state, ignoring filters to confirm regardless of caller context.
        using (IdentityDbContext ctx = new IdentityDbContext(options, TenantMock(tenantA).Object))
        {
            Invitation invitation = await ctx.Invitations
                .IgnoreQueryFilters()
                .SingleAsync(i => i.Token == token);

            invitation.IsAccepted.Should().BeTrue();
            invitation.AcceptedByUserId.Should().Be(inviteeUserId);

            bool membershipExists = await ctx.TenantMemberships
                .AnyAsync(m => m.UserId == inviteeUserId && m.TenantId == tenantA && m.IsActive);

            membershipExists.Should().BeTrue("accepting the invitation must grant TenantA membership");
        }
    }

    [Fact]
    public async Task AcceptInvitation_BrandNewUserWithNoCurrentTenant_Succeeds()
    {
        // First-ever tenant via invite instead of CreateTenant: the accepting user's
        // JWT has no tenantId claim at all (fresh setup token, same state as
        // Register/CreateTenant flow validated earlier).
        DbContextOptions<IdentityDbContext> options = BuildOptions();
        (Guid tenantA, Guid adminUserId) = await SeedTenantWithAdminAsync(options);

        string token;
        Guid inviteeUserId = Guid.NewGuid();

        using (IdentityDbContext ctx = new IdentityDbContext(options, TenantMock(tenantA).Object))
        {
            CreateInvitationCommandHandler createHandler = new CreateInvitationCommandHandler(
                new InvitationRepository(ctx),
                new TenantMembershipRepository(ctx),
                new UserRepository(ctx));

            CreateInvitationResponse created = await createHandler.Handle(
                new CreateInvitationCommand
                {
                    TenantId = tenantA,
                    Email = "newbie@example.com",
                    Role = Role.Manager,
                    InvitedByUserId = adminUserId,
                },
                CancellationToken.None);

            token = created.Token;
        }

        using (IdentityDbContext ctx = new IdentityDbContext(options, TenantMock(null).Object))
        {
            ctx.Users.Add(new User
            {
                Id = inviteeUserId,
                ExternalId = "newbie@example.com",
                IdentityProvider = "Local",
                Email = "newbie@example.com",
                PasswordHash = "hash",
                CreatedAt = DateTimeOffset.UtcNow,
                IsActive = true,
            });
            await ctx.SaveChangesAsync();
        }

        using (IdentityDbContext ctx = new IdentityDbContext(options, TenantMock(null).Object))
        {
            AcceptInvitationCommandHandler acceptHandler = new AcceptInvitationCommandHandler(
                new InvitationRepository(ctx),
                new TenantMembershipRepository(ctx),
                new UserRepository(ctx),
                FakeJwt().Object);

            Func<Task<AcceptInvitationResponse>> act = () => acceptHandler.Handle(
                new AcceptInvitationCommand { Token = token, UserId = inviteeUserId },
                CancellationToken.None);

            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task RevokeInvitation_AdminActingInOwnTenant_Succeeds()
    {
        DbContextOptions<IdentityDbContext> options = BuildOptions();
        (Guid tenantId, Guid adminUserId) = await SeedTenantWithAdminAsync(options);

        Guid invitationId;

        using (IdentityDbContext ctx = new IdentityDbContext(options, TenantMock(tenantId).Object))
        {
            CreateInvitationCommandHandler createHandler = new CreateInvitationCommandHandler(
                new InvitationRepository(ctx),
                new TenantMembershipRepository(ctx),
                new UserRepository(ctx));

            CreateInvitationResponse created = await createHandler.Handle(
                new CreateInvitationCommand
                {
                    TenantId = tenantId,
                    Email = "invitee@example.com",
                    Role = Role.Manager,
                    InvitedByUserId = adminUserId,
                },
                CancellationToken.None);

            invitationId = created.InvitationId;
        }

        using (IdentityDbContext ctx = new IdentityDbContext(options, TenantMock(tenantId).Object))
        {
            RevokeInvitationCommandHandler revokeHandler = new RevokeInvitationCommandHandler(
                new InvitationRepository(ctx),
                new TenantMembershipRepository(ctx));

            RevokeInvitationResponse response = await revokeHandler.Handle(
                new RevokeInvitationCommand
                {
                    InvitationId = invitationId,
                    TenantId = tenantId,
                    RevokedByUserId = adminUserId,
                },
                CancellationToken.None);

            response.Success.Should().BeTrue();
        }

        using (IdentityDbContext ctx = new IdentityDbContext(options, TenantMock(tenantId).Object))
        {
            bool stillExists = await ctx.Invitations
                .IgnoreQueryFilters()
                .AnyAsync(i => i.Id == invitationId);

            stillExists.Should().BeFalse();
        }
    }

    [Fact]
    public async Task RevokeInvitation_CrossTenantAttempt_FailsSafelyWithoutLeakingExistence()
    {
        // TenantB's admin tries to revoke an invitation that belongs to TenantA, by
        // guessing/reusing an invitationId. The query filter now hides the row before
        // the handler's own manual TenantId check even runs.
        DbContextOptions<IdentityDbContext> options = BuildOptions();
        (Guid tenantA, Guid adminA) = await SeedTenantWithAdminAsync(options);
        (Guid tenantB, Guid adminB) = await SeedTenantWithAdminAsync(options);

        Guid invitationId;

        using (IdentityDbContext ctx = new IdentityDbContext(options, TenantMock(tenantA).Object))
        {
            CreateInvitationCommandHandler createHandler = new CreateInvitationCommandHandler(
                new InvitationRepository(ctx),
                new TenantMembershipRepository(ctx),
                new UserRepository(ctx));

            CreateInvitationResponse created = await createHandler.Handle(
                new CreateInvitationCommand
                {
                    TenantId = tenantA,
                    Email = "invitee@example.com",
                    Role = Role.Manager,
                    InvitedByUserId = adminA,
                },
                CancellationToken.None);

            invitationId = created.InvitationId;
        }

        using (IdentityDbContext ctx = new IdentityDbContext(options, TenantMock(tenantB).Object))
        {
            RevokeInvitationCommandHandler revokeHandler = new RevokeInvitationCommandHandler(
                new InvitationRepository(ctx),
                new TenantMembershipRepository(ctx));

            Func<Task> act = () => revokeHandler.Handle(
                new RevokeInvitationCommand
                {
                    InvitationId = invitationId,
                    TenantId = tenantB,
                    RevokedByUserId = adminB,
                },
                CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        // The invitation must still exist untouched in TenantA.
        using (IdentityDbContext ctx = new IdentityDbContext(options, TenantMock(tenantA).Object))
        {
            bool stillExists = await ctx.Invitations.AnyAsync(i => i.Id == invitationId);
            stillExists.Should().BeTrue("a foreign tenant's admin must not be able to revoke this invitation");
        }
    }
}
