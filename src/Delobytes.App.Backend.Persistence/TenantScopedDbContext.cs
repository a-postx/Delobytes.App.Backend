using System.Linq.Expressions;
using System.Reflection;
using System.Security;
using Delobytes.App.Backend.Contracts.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Delobytes.App.Backend.Persistence;

/// <summary>
/// Base EF Core DbContext for module databases that enforce multi-tenancy. Attaches a tenant
/// query filter to every <see cref="ITenantScoped"/> entity, stamps new/reparented rows with the
/// current tenant on save, and rejects writes that cross tenant boundaries.
/// </summary>
public abstract class TenantScopedDbContext : DbContext
{
    /// <summary>
    /// Tenant context resolved for the current request/message. Accessed by reflection from
    /// <see cref="BuildTenantIdFilter"/> so the query filter reads the live scoped instance
    /// instead of the value captured when the singleton model cache was built.
    /// </summary>
    protected readonly ITenantContext TenantContext;

    protected TenantScopedDbContext(DbContextOptions options, ITenantContext tenantContext)
        : base(options)
    {
        TenantContext = tenantContext;
    }

    /// <summary>
    /// Registers the shared model conventions (tenant shadow property, audit properties,
    /// RowVersion/xmin mapping, UTC <see cref="DateTimeOffset"/> conversion). Override with an
    /// empty body to opt a module out (see <c>IdentityDbContext</c>, which currently does not use
    /// these conventions and must not start using them silently as a side effect of deduplication).
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddSharedModelConventions();
    }

    /// <summary>
    /// Declares/reuses the <c>TenantId</c> property on every <see cref="ITenantScoped"/> entity and
    /// attaches a query filter comparing it to the current <see cref="ITenantContext.TenantId"/>.
    /// Call this from the derived context's <c>OnModelCreating</c>, after
    /// <c>ApplyConfigurationsFromAssembly</c>.
    /// </summary>
    protected void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            // Some ITenantScoped entities (e.g. Invitation, JobSchedule) already declare TenantId
            // as a real, non-nullable Guid property via their own IEntityTypeConfiguration. Reuse
            // it instead of adding a conflicting shadow property with the same name.
            IMutableProperty tenantIdProperty = entityType.FindProperty("TenantId")
                ?? modelBuilder.Entity(entityType.ClrType)
                    .Property<Guid?>("TenantId")
                    .IsRequired()
                    .Metadata;

            bool hasSingleColumnTenantIndex = entityType.GetIndexes()
                .Any(i => i.Properties.Count == 1 && i.Properties[0].Name == "TenantId");

            if (!hasSingleColumnTenantIndex)
            {
                modelBuilder.Entity(entityType.ClrType).HasIndex("TenantId");
            }

            modelBuilder.Entity(entityType.ClrType)
                .HasQueryFilter(BuildTenantIdFilter(entityType.ClrType, tenantIdProperty.ClrType));
        }
    }

    // Builds `e => EF.Property<TProperty>(e, "TenantId") == TenantContext.TenantId`, widening a
    // real non-nullable Guid TenantId to Guid? so it can be compared against
    // ITenantContext.TenantId (Guid?).
    private LambdaExpression BuildTenantIdFilter(Type entityClrType, Type tenantIdClrType)
    {
        ParameterExpression parameter = Expression.Parameter(entityClrType, "e");

        MethodInfo efPropertyMethod = typeof(EF)
            .GetMethod(nameof(EF.Property), BindingFlags.Static | BindingFlags.Public)!
            .MakeGenericMethod(tenantIdClrType);

        MethodCallExpression tenantIdAccess = Expression.Call(
            efPropertyMethod,
            parameter,
            Expression.Constant("TenantId"));

        Expression tenantIdAsNullable = tenantIdClrType == typeof(Guid?)
            ? tenantIdAccess
            : Expression.Convert(tenantIdAccess, typeof(Guid?));

        // Expression.Constant(this) captures the DbContext instance reference. EF Core recognises
        // DbContext-typed constants in query filter trees and substitutes the *current* instance at
        // query execution time, so TenantId is read from the scoped ITenantContext on every request.
        ConstantExpression contextRef = Expression.Constant(this, GetType());
        FieldInfo tenantContextField = typeof(TenantScopedDbContext)
            .GetField(nameof(TenantContext), BindingFlags.NonPublic | BindingFlags.Instance)!;
        MemberExpression tenantContextAccess = Expression.Field(contextRef, tenantContextField);
        MemberExpression tenantContextTenantId = Expression.Property(
            tenantContextAccess,
            nameof(ITenantContext.TenantId));

        BinaryExpression comparison = Expression.Equal(tenantIdAsNullable, tenantContextTenantId);
        return Expression.Lambda(comparison, parameter);
    }

    /// <inheritdoc/>
    public override int SaveChanges()
    {
        SetTenantId();
        ValidateCrossTenantWrite();
        return base.SaveChanges();
    }

    /// <inheritdoc/>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SetTenantId();
        ValidateCrossTenantWrite();
        return base.SaveChangesAsync(cancellationToken);
    }

    // Guards against orphan rows: without this check, a missing tenant (e.g. a background job or
    // message consumer with no HttpContext) would leave the shadow TenantId at its CLR default
    // (Guid.Empty) instead of throwing. Also reparents Modified entries whose TenantId is still
    // empty to Added: EF Core sometimes tracks a brand-new entity as Modified when it is added
    // to a tracked collection with its Id already set, which would otherwise raise
    // a concurrency exception against a non-existent row.
    private void SetTenantId()
    {
        List<EntityEntry> addedScopedEntries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added && e.Entity is ITenantScoped)
            .ToList();

        List<EntityEntry> modifiedScopedEntries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Modified && e.Entity is ITenantScoped)
            .ToList();

        if (addedScopedEntries.Count == 0 && modifiedScopedEntries.Count == 0)
        {
            return;
        }

        Guid? tenantId = TenantContext.TenantId;

        if (!tenantId.HasValue)
        {
            // Entries exempted from cross-tenant validation (see IsExemptFromCrossTenantValidation,
            // e.g. Invitation accepted by a user with no current tenant) legitimately keep their
            // own already-set TenantId.
            List<string> entityNames = addedScopedEntries
                .Concat(modifiedScopedEntries)
                .Where(e => !IsExemptFromCrossTenantValidation(e))
                .Select(e => e.Entity.GetType().Name)
                .Distinct()
                .ToList();

            if (entityNames.Count == 0)
            {
                return;
            }

            throw new SecurityException(
                $"Cannot save tenant-scoped entit{(entityNames.Count == 1 ? "y" : "ies")} ({string.Join(", ", entityNames)}) " +
                "without a resolved TenantId. The current execution context has no tenant.");
        }

        foreach (EntityEntry entry in addedScopedEntries)
        {
            entry.Property("TenantId").CurrentValue = tenantId.Value;
        }

        foreach (EntityEntry entry in modifiedScopedEntries)
        {
            PropertyEntry tenantIdProperty = entry.Property("TenantId");
            Guid? entityTenantId = (Guid?)tenantIdProperty.CurrentValue;

            if (!entityTenantId.HasValue || entityTenantId.Value == Guid.Empty)
            {
                entry.State = EntityState.Added;
                tenantIdProperty.CurrentValue = tenantId.Value;
            }
        }
    }

    // Prevents writes to entities belonging to a different tenant. Added entities are already
    // protected by SetTenantId() overwriting the value. This guard targets Modified and Deleted: an
    // entity loaded via IgnoreQueryFilters() or with a manually-tampered shadow property would
    // otherwise pass through undetected.
    private void ValidateCrossTenantWrite()
    {
        Guid? tenantId = TenantContext.TenantId;
        if (!tenantId.HasValue)
        {
            // System / background context -- no user-scoped enforcement.
            return;
        }

        IEnumerable<EntityEntry> entries = ChangeTracker.Entries()
            .Where(e =>
                (e.State == EntityState.Modified || e.State == EntityState.Deleted)
                && e.Entity is ITenantScoped
                && !IsExemptFromCrossTenantValidation(e));

        foreach (EntityEntry entry in entries)
        {
            Guid? entityTenantId = (Guid?)entry.Property("TenantId").CurrentValue;

            if (entityTenantId.HasValue && entityTenantId.Value != tenantId.Value)
            {
                throw new SecurityException(
                    $"Cross-tenant write detected on '{entry.Entity.GetType().Name}': " +
                    $"entity TenantId '{entityTenantId}' does not match current tenant '{tenantId}'.");
            }
        }
    }

    /// <summary>
    /// Override to exclude specific entities from the cross-tenant write guard (e.g. a record that
    /// is legitimately modified by a user who is not yet a member of its tenant).
    /// </summary>
    protected virtual bool IsExemptFromCrossTenantValidation(EntityEntry entry) => false;
}
