namespace Delobytes.App.Backend.Catalog.Application.Interfaces;

/// <summary>
/// Transaction of a single catalog operation that must span a number allocation and the insert
/// it belongs to. Numbers stay durable only while the surrounding transaction does, so a caller
/// that allocates a code owns one of these.
/// </summary>
public interface ICatalogTransaction : IAsyncDisposable
{
    /// <summary>Commits the transaction.</summary>
    public Task CommitAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Rolls the transaction back. A transaction that already committed or was already disposed
    /// is left alone rather than reported as an error: a failure after commit is not a failed
    /// rollback.
    /// </summary>
    public Task RollbackAsync(CancellationToken cancellationToken);
}
