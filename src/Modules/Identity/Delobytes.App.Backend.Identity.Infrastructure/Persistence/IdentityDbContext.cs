using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Delobytes.App.Backend.Identity.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the Identity module.
/// </summary>
public class IdentityDbContext : TenantScopedDbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IdentityDbContext"/> class.
    /// </summary>
    /// <param name="options">DbContext options.</param>
    /// <param name="tenantContext">Tenant context for query filtering.</param>
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext)
    {
    }

    /// <summary>
    /// Gets or sets the Tenants entity set.
    /// </summary>
    public DbSet<Tenant> Tenants => Set<Tenant>();

    /// <summary>
    /// Gets or sets the Users entity set.
    /// </summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>
    /// Gets or sets the TenantMemberships entity set.
    /// </summary>
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();

    /// <summary>
    /// Gets or sets the Invitations entity set.
    /// </summary>
    public DbSet<Invitation> Invitations => Set<Invitation>();

    /// <summary>
    /// Gets or sets the TenantTaxProfiles entity set.
    /// </summary>
    public DbSet<TenantTaxProfile> TenantTaxProfiles => Set<TenantTaxProfile>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Schema isolation for Identity module
        modelBuilder.HasDefaultSchema("identity");

        // Apply all entity type configurations from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);

        ApplyTenantQueryFilters(modelBuilder);
    }

    // Invitation is deliberately exempted: AcceptInvitationCommandHandler loads it via
    // FindByTokenAsync's IgnoreQueryFilters() and legitimately modifies it (IsAccepted,
    // AcceptedAt, AcceptedByUserId) while the accepting user's current tenant context is some
    // OTHER tenant (or none at all) -- that is the whole mechanism by which a user gains access
    // to a tenant they are not yet a member of.
    protected override bool IsExemptFromCrossTenantValidation(EntityEntry entry)
    {
        return entry.Entity is Invitation && entry.State == EntityState.Modified;
    }
}
