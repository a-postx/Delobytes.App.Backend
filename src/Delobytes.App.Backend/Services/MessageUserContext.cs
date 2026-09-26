using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Services;

/// <summary>
/// User context for message-based execution (MassTransit consumers).
/// Mirrors <see cref="MessageTenantContext"/>: set by <c>UserConsumeFilter</c>
/// for the duration of message processing and cleared afterwards to prevent leakage.
/// </summary>
public class MessageUserContext : IUserContext
{
    private static readonly AsyncLocal<Guid?> _asyncLocalUserId = new AsyncLocal<Guid?>();

    /// <inheritdoc/>
    public Guid? UserId => _asyncLocalUserId.Value;

    /// <summary>
    /// Sets the current user identifier for the message processing scope.
    /// </summary>
    /// <param name="userId">User identifier, or null for system-initiated messages.</param>
    public void SetUserId(Guid? userId)
    {
        _asyncLocalUserId.Value = userId;
    }

    /// <summary>
    /// Clears the current user identifier after message processing.
    /// </summary>
    public void Clear()
    {
        _asyncLocalUserId.Value = null;
    }
}
