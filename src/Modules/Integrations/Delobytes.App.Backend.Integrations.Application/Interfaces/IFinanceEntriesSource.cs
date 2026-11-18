using Delobytes.App.Backend.Integrations.Application.Models;

namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// A channel source of finance entries. Takes a window and an opaque cursor and returns the next
/// page, the cursor to continue from and the mapping diagnostics.
/// </summary>
public interface IFinanceEntriesSource
{
    /// <summary>
    /// Reads one page of the channel's finance document.
    /// </summary>
    /// <param name="connectionId">Connection to authenticate as.</param>
    /// <param name="request">Window and cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page, its next cursor and the mapping diagnostics.</returns>
    public Task<FinanceEntriesPage> GetFinanceEntriesAsync(
        Guid connectionId,
        FinanceEntriesRequest request,
        CancellationToken cancellationToken);
}
