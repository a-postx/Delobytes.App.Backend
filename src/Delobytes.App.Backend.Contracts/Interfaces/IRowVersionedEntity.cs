namespace Delobytes.App.Backend.Contracts.Interfaces;

/// <summary>
/// Marker interface for entities that use row versioning for optimistic concurrency control.
/// The RowVersion property will be configured as a PostgreSQL xmin system column.
/// </summary>
public interface IRowVersionedEntity
{
    /// <summary>
    /// Gets or sets the row version for optimistic concurrency.
    /// Automatically managed by the database (PostgreSQL xmin).
    /// </summary>
    public uint RowVersion { get; set; }
}
