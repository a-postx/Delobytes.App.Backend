namespace Delobytes.App.Backend.Contracts.Interfaces;

/// <summary>
/// Provides access to the current user context.
/// Resolved from the JWT "sub" claim for HTTP requests, or from a message header
/// in MassTransit consumers via <c>UserConsumeFilter</c>.
/// Returns null when the operation is not user-initiated (background jobs, seeders,
/// system-triggered message consumers) — this is a valid state, not an error.
/// </summary>
public interface IUserContext
{
    /// <summary>
    /// Gets the current user identifier, or null when no authenticated user is present.
    /// </summary>
    public Guid? UserId { get; }
}
