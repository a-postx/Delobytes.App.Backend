using Delobytes.App.Backend.Contracts.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Delobytes.App.Backend.Persistence.Interceptors;

/// <summary>
/// EF Core SaveChangesInterceptor that automatically sets audit timestamps
/// and the identifiers of the user responsible for the operation.
/// Reads <see cref="IUserContext"/> at save time — never at construction time —
/// so the value is always current, even when the DbContext is long-lived.
/// Shared across modules: any module DbContext may register this interceptor
/// instead of each module defining its own copy.
/// </summary>
public sealed class AuditableEntityInterceptor : SaveChangesInterceptor
{
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditableEntityInterceptor"/> class.
    /// </summary>
    /// <param name="userContext">Current user context.</param>
    /// <param name="timeProvider">Time provider used to stamp audit timestamps.</param>
    public AuditableEntityInterceptor(IUserContext userContext, TimeProvider timeProvider)
    {
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        UpdateAuditableEntities(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        UpdateAuditableEntities(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void UpdateAuditableEntities(DbContext? context)
    {
        if (context == null)
        {
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        Guid? userId = _userContext.UserId;

        IEnumerable<EntityEntry<IAuditableEntity>> entries = context.ChangeTracker
            .Entries<IAuditableEntity>();

        foreach (EntityEntry<IAuditableEntity> entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = null;
                entry.Entity.CreatedByUserId = userId;
                entry.Entity.UpdatedByUserId = null;
            }
            else if (entry.State == EntityState.Modified)
            {
                // Prevent accidental overwrites of immutable creation fields.
                entry.Property(nameof(IAuditableEntity.CreatedAt)).IsModified = false;
                entry.Property(nameof(IAuditableEntity.CreatedByUserId)).IsModified = false;

                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedByUserId = userId;
            }
        }
    }
}
