using Delobytes.App.Backend.Identity.Application.Commands;
using Delobytes.App.Backend.Identity.Domain.Entities;

namespace Delobytes.App.Backend.Identity.Application.Interfaces;

/// <summary>
/// Issues the local session for an authenticated user: picks the active tenant,
/// records the login, and signs a JWT. Shared by every authentication entry point
/// so that password login, registration and OAuth callbacks cannot drift apart.
/// </summary>
public interface IUserSessionIssuer
{
    /// <summary>
    /// Persists the login state of the user and returns the signed session.
    /// </summary>
    /// <param name="user">The authenticated user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Session with a JWT, or a temporary token when the user has no tenant yet.</returns>
    public Task<LoginResponse> IssueAsync(User user, CancellationToken cancellationToken);
}
