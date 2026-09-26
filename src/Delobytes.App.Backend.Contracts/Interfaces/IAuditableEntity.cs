namespace Delobytes.App.Backend.Contracts.Interfaces;

/// <summary>
/// Marker interface for entities that track creation and modification timestamps
/// and the identity of the user who performed each operation.
/// EF Core SaveChangesInterceptor will automatically populate these properties.
/// </summary>
public interface IAuditableEntity
{
    /// <summary>
    /// Gets or sets the UTC date and time when the entity was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time when the entity was last modified.
    /// Null if the entity has never been modified after creation.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user who created the entity.
    /// Null for system-initiated operations (background jobs, seeders, message consumers
    /// without a user context). Nullable by design — NOT NULL would be a false guarantee.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user who last modified the entity.
    /// Null when the entity has never been modified, or when the last modification
    /// was system-initiated.
    /// </summary>
    public Guid? UpdatedByUserId { get; set; }
}
