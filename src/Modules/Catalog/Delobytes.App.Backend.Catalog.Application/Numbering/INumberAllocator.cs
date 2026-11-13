namespace Delobytes.App.Backend.Catalog.Application.Numbering;

/// <summary>
/// Composes the next identifier of a numbering scheme.
/// </summary>
public interface INumberAllocator
{
    /// <summary>
    /// Issues the next identifier of the scheme and advances its counters.
    /// Requires an ambient transaction: row locks taken by the store are released at that
    /// transaction's commit, and this method never opens, commits or rolls back one itself.
    /// </summary>
    public Task<string> AllocateAsync(Guid schemeId, CancellationToken cancellationToken);
}
